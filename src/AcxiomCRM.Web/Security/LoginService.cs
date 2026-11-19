using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AcxiomCRM.Web.Security;

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    LockedOut,
    Inactive
}

public sealed record LoginOutcome(LoginStatus Status, ApplicationUser? User = null, DateTimeOffset? LockoutEnd = null, int RemainingAttempts = 0);

/// <summary>
/// Shared credential check for the MVC login page (cookie) and the API (/api/auth/login, JWT).
/// Applies lockout on failure, resets the failure counter on success and audits every outcome.
/// Passwords are never logged.
/// </summary>
public sealed class LoginService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IAuditService audit,
    IBusinessClock clock,
    IOptions<SecurityOptions> security)
{
    public async Task<ApplicationUser?> FindAsync(string userNameOrEmail)
    {
        var key = userNameOrEmail.Trim();
        return key.Contains('@')
            ? await users.FindByEmailAsync(key) ?? await users.FindByNameAsync(key)
            : await users.FindByNameAsync(key);
    }

    public async Task<LoginOutcome> LoginAsync(string userNameOrEmail, string password, bool issueCookie, bool persistent = false)
    {
        var attempted = Truncate(userNameOrEmail.Trim(), 256);
        var user = await FindAsync(userNameOrEmail);
        if (user is null)
        {
            await audit.LogAsync(AuditModule.Authentication, AuditAction.LoginFailed, AuditResult.Failure,
                details: "Unknown user name or email.", userName: attempted);
            return new LoginOutcome(LoginStatus.InvalidCredentials);
        }

        var wasLocked = await users.IsLockedOutAsync(user);
        var result = issueCookie
            ? await signIn.PasswordSignInAsync(user, password, persistent, lockoutOnFailure: true)
            : await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            user.LastLoginDate = clock.UtcNow;
            await users.UpdateAsync(user);
            await audit.LogAsync(AuditModule.Authentication, AuditAction.Login, details: issueCookie ? "Web sign-in." : "API token issued.",
                userId: user.Id, userName: user.UserName, entityName: "User", recordId: user.Id);
            return new LoginOutcome(LoginStatus.Success, user);
        }

        if (result.IsLockedOut)
        {
            var end = await users.GetLockoutEndDateAsync(user);
            await audit.LogAsync(AuditModule.Authentication, wasLocked ? AuditAction.LoginFailed : AuditAction.Lockout, AuditResult.Failure,
                details: wasLocked ? "Sign-in attempted while the account is locked." :
                    $"Account locked after {security.Value.Lockout.MaxFailedAccessAttempts} failed attempts until {end:u}.",
                userId: user.Id, userName: user.UserName, entityName: "User", recordId: user.Id);
            return new LoginOutcome(LoginStatus.LockedOut, user, end);
        }

        if (result.IsNotAllowed)
        {
            await audit.LogAsync(AuditModule.Authentication, AuditAction.LoginFailed, AuditResult.Failure,
                details: "Sign-in blocked: account is inactive.", userId: user.Id, userName: user.UserName, entityName: "User", recordId: user.Id);
            return new LoginOutcome(LoginStatus.Inactive, user);
        }

        var failed = await users.GetAccessFailedCountAsync(user);
        var remaining = Math.Max(0, security.Value.Lockout.MaxFailedAccessAttempts - failed);
        await audit.LogAsync(AuditModule.Authentication, AuditAction.LoginFailed, AuditResult.Failure,
            details: $"Invalid password ({failed} consecutive failure(s)).", userId: user.Id, userName: user.UserName, entityName: "User", recordId: user.Id);
        return new LoginOutcome(LoginStatus.InvalidCredentials, user, RemainingAttempts: remaining);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
