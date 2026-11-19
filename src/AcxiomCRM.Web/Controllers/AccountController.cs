using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Services;
using AcxiomCRM.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AcxiomCRM.Web.Controllers;

/// <summary>Login, registration, logout, password change and access-denied handling.</summary>
public sealed class AccountController(
    LoginService login,
    SignInManager<ApplicationUser> signIn,
    UserManager<ApplicationUser> users,
    IAuditService audit,
    IPasswordHistoryService passwordHistory,
    IOptions<SecurityOptions> security,
    IBusinessClock clock) : CrmController
{
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        ViewData["AllowRegistration"] = security.Value.AllowSelfRegistration;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["AllowRegistration"] = security.Value.AllowSelfRegistration;
        if (!ModelState.IsValid) return View(model);

        var outcome = await login.LoginAsync(model.UserNameOrEmail!, model.Password!, issueCookie: true, persistent: model.RememberMe);
        switch (outcome.Status)
        {
            case LoginStatus.Success:
                if (outcome.User!.MustChangePassword) return RedirectToAction(nameof(ChangePassword));
                return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)
                    ? LocalRedirect(model.ReturnUrl)
                    : RedirectToAction("Index", "Home");
            case LoginStatus.LockedOut:
                var until = outcome.LockoutEnd is { } end ? clock.ToLocal(end.UtcDateTime) : clock.Now.AddMinutes(security.Value.Lockout.LockoutMinutes);
                ModelState.AddModelError(string.Empty,
                    $"This account is locked because of repeated failed sign-in attempts. Try again after {until:hh:mm tt} or contact an administrator.");
                break;
            default:
                ModelState.AddModelError(string.Empty, outcome.RemainingAttempts is > 0 and <= 2
                    ? $"Invalid login attempt. {outcome.RemainingAttempts} attempt(s) remaining before the account is locked."
                    : "Invalid login attempt.");
                break;
        }
        model.Password = null;
        return View(model);
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Register()
    {
        if (!security.Value.AllowSelfRegistration) return NotFound();
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new RegisterViewModel());
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!security.Value.AllowSelfRegistration) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var email = model.Email!.Trim().ToLowerInvariant();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = model.FullName!.Trim(),
            IsActive = true,
            CreatedDate = clock.UtcNow,
            LockoutEnabled = true
        };
        var result = await users.CreateAsync(user, model.Password!);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(e.Code.Contains("Email") || e.Code.Contains("UserName") ? nameof(model.Email) : nameof(model.Password),
                    e.Code is "DuplicateUserName" or "DuplicateEmail" ? "An account with this email already exists." : e.Description);
            return View(model);
        }

        // Self-registered users get the least-privileged role; an Admin can promote them later.
        await users.AddToRoleAsync(user, AppRoles.SalesExecutive);
        await passwordHistory.RecordAsync(user);
        await audit.LogAsync(AuditModule.Authentication, AuditAction.Register, entityName: "User", recordId: user.Id,
            details: "Self-registration (Sales Executive).", userId: user.Id, userName: user.UserName);

        await signIn.SignInAsync(user, isPersistent: false);
        user.LastLoginDate = clock.UtcNow;
        await users.UpdateAsync(user);
        await audit.LogAsync(AuditModule.Authentication, AuditAction.Login, details: "Web sign-in after registration.", userId: user.Id, userName: user.UserName);
        Success($"Welcome to AcxiomCRM, {user.FullName}!");
        return RedirectToAction("Index", "Home");
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await audit.LogAsync(AuditModule.Authentication, AuditAction.Logout, details: "Web sign-out.");
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, Authorize]
    public IActionResult ChangePassword() =>
        View(new ChangePasswordViewModel { IsForced = User.HasClaim(AppClaimTypes.MustChangePassword, "true") });

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        model.IsForced = User.HasClaim(AppClaimTypes.MustChangePassword, "true");
        if (!ModelState.IsValid) return View(model);

        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        var result = await users.ChangePasswordAsync(user, model.CurrentPassword!, model.NewPassword!);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(e.Code == "PasswordMismatch" ? nameof(model.CurrentPassword) : nameof(model.NewPassword),
                    e.Code == "PasswordMismatch" ? "The current password is incorrect." : e.Description);
            await audit.LogAsync(AuditModule.Security, AuditAction.PasswordChange, AuditResult.Failure, entityName: "User", recordId: user.Id,
                details: "Password change rejected.");
            return View(model);
        }

        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        await passwordHistory.RecordAsync(user);
        await signIn.RefreshSignInAsync(user);
        await audit.LogAsync(AuditModule.Security, AuditAction.PasswordChange, entityName: "User", recordId: user.Id, details: "Password changed by user.");
        Success("Your password has been changed.");
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous, IgnoreAntiforgeryToken]
    public async Task<IActionResult> AccessDenied(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            await audit.LogAsync(AuditModule.Security, AuditAction.AccessDenied, AuditResult.Failure,
                details: $"Access denied to {Truncate(returnUrl ?? "(unknown)", 200)}");
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
