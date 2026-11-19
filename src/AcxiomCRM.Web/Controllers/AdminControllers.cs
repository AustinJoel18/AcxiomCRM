using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Services;
using AcxiomCRM.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AcxiomCRM.Web.Controllers;

/// <summary>User administration. Admin: full. Manager: read-only view of own team.</summary>
[Authorize(Policy = Policies.UserDirectory)]
public sealed class UsersController(IUserAdminService userAdmin, IAuditService audit, ILookupService lookups, IDataScope scope) : CrmController
{
    public async Task<IActionResult> Index([FromQuery] UserQuery query, CancellationToken ct)
    {
        ViewData["CanManage"] = scope.IsAdmin;
        return View(new ListPageViewModel<UserDto, UserQuery> { Query = query, Results = await userAdmin.SearchAsync(query, ct), Scope = scope.IsAdmin ? "All users" : "Team" });
    }

    public async Task<IActionResult> Details(string id, CancellationToken ct)
    {
        var result = await userAdmin.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        var history = scope.IsAdmin
            ? (await audit.SearchAsync(new AuditQuery { EntityName = "User", RecordId = id, PageSize = 25 }, ct)).Items
            : [];
        return View(new UserDetailsViewModel { User = result.Value!, History = history, CanManage = scope.IsAdmin, IsSelf = id == scope.UserId });
    }

    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new UserFormViewModel { Role = AppRoles.SalesExecutive, IsActive = true, MustChangePassword = true };
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Create(UserFormViewModel vm, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await userAdmin.CreateAsync(vm, ct);
            if (result.Succeeded)
            {
                Success($"User {result.Value!.Email} created.");
                return RedirectToAction(nameof(Details), new { id = result.Value.Id });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result);
        }
        vm.Password = vm.ConfirmPassword = null;
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var result = await userAdmin.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        var u = result.Value!;
        var vm = new UserFormViewModel
        {
            Id = u.Id, FullName = u.FullName, Email = u.Email, PhoneNumber = u.PhoneNumber, Role = u.Role, ManagerId = u.ManagerId, IsActive = u.IsActive
        };
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Edit(string id, UserFormViewModel vm, CancellationToken ct)
    {
        vm.Id = id;
        foreach (var key in new[] { nameof(vm.Password), nameof(vm.ConfirmPassword) }) ModelState.Remove(key);
        if (ModelState.IsValid)
        {
            var result = await userAdmin.UpdateAsync(id, vm, ct);
            if (result.Succeeded)
            {
                Success("User updated.");
                return RedirectToAction(nameof(Details), new { id });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result);
        }
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> SetActive(string id, bool active, CancellationToken ct)
    {
        var result = await userAdmin.SetActiveAsync(id, active, ct);
        if (Deny(result) is { } denied) return denied;
        if (result.Succeeded) Success(active ? "User activated." : "User deactivated. Active sessions will be terminated.");
        else Failure(result.Message ?? "The change could not be applied.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Unlock(string id, CancellationToken ct)
    {
        var result = await userAdmin.UnlockAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        Success("Account unlocked.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> ResetPassword(string id, [Bind(Prefix = "ResetPassword")] AdminResetPasswordRequest request, CancellationToken ct)
    {
        var result = await userAdmin.ResetPasswordAsync(id, request, ct);
        if (Deny(result) is { } denied) return denied;
        if (result.Succeeded) Success("Password reset. Share the temporary password with the user through a secure channel.");
        else Failure(string.Join(" ", result.Errors.Values.SelectMany(v => v)));
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PopulateAsync(UserFormViewModel vm, CancellationToken ct)
    {
        vm.Managers = (await lookups.ManagersAsync(ct)).Where(m => m.Value != vm.Id).ToSelectList(vm.ManagerId);
        vm.Roles = AppRoles.All.Select(r => new SelectListItem(AppRoles.DisplayName(r), r, r == vm.Role));
    }
}

/// <summary>Role catalogue and permission matrix (roles are fixed: one primary role per user).</summary>
[Authorize(Policy = Policies.AdminOnly)]
public sealed class RolesController(IUserAdminService userAdmin) : CrmController
{
    public static readonly IReadOnlyList<(string Module, string Admin, string Manager, string Sales)> Matrix =
    [
        ("Dashboard", "Full", "Team", "Own/Assigned"),
        ("Customers", "Full", "Team", "Own/Assigned"),
        ("Leads", "Full", "Team", "Own/Assigned"),
        ("Follow-Ups", "Full", "Team", "Own/Assigned"),
        ("Opportunities", "Full", "Team", "Own/Assigned"),
        ("Activities", "Full", "Team", "Own/Assigned"),
        ("Delete CRM records", "Yes", "Team", "No"),
        ("User Management", "Full", "View team", "No"),
        ("Role Management", "Full", "No", "No"),
        ("Audit Log", "Full", "Limited (team, business events)", "No"),
        ("REST API", "Authorized endpoints", "Authorized endpoints", "Authorized endpoints"),
        ("Reports", "All", "Management / team reports", "Own/assigned reports")
    ];

    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(new RolesViewModel { Roles = await userAdmin.RolesAsync(ct), Matrix = Matrix });
}

/// <summary>Append-only audit trail viewer (no edit/delete actions exist).</summary>
[Authorize(Policy = Policies.AuditViewer)]
public sealed class AuditLogsController(IAuditService audit, ILookupService lookups, IDataScope scope) : CrmController
{
    public async Task<IActionResult> Index([FromQuery] AuditQuery query, CancellationToken ct)
    {
        if (query.PageSize == 10) query.PageSize = 25;
        return View(new AuditIndexViewModel
        {
            Query = query,
            Results = await audit.SearchAsync(query, ct),
            Users = (await lookups.VisibleUsersAsync(ct)).ToSelectList(query.UserId),
            Limited = !scope.IsAdmin
        });
    }

    public async Task<IActionResult> Details(long id, CancellationToken ct)
    {
        var entry = await audit.GetAsync(id, ct);
        return entry is null ? NotFound() : View(entry);
    }
}

/// <summary>Global search across customers, leads and opportunities in the caller's scope.</summary>
[Authorize(Policy = Policies.CrmUser)]
public sealed class SearchController(ICustomerService customers, ILeadService leads, IOpportunityService opportunities) : CrmController
{
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return View(new SearchViewModel());
        return View(new SearchViewModel
        {
            Q = q,
            Customers = (await customers.SearchAsync(new CustomerQuery { Search = q, PageSize = 10 }, ct)).Items,
            Leads = (await leads.SearchAsync(new LeadQuery { Search = q, PageSize = 10 }, ct)).Items,
            Opportunities = (await opportunities.SearchAsync(new OpportunityQuery { Search = q, PageSize = 10 }, ct)).Items
        });
    }
}
