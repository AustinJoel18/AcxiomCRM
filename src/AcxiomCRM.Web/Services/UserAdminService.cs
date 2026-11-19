using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Web.Services;

public interface IUserAdminService
{
    Task<PagedResult<UserDto>> SearchAsync(UserQuery query, CancellationToken ct = default);
    Task<ServiceResult<UserDto>> GetAsync(string id, CancellationToken ct = default);
    Task<ServiceResult<UserDto>> CreateAsync(UserCreateRequest request, CancellationToken ct = default);
    Task<ServiceResult<UserDto>> UpdateAsync(string id, UserUpdateRequest request, CancellationToken ct = default);
    Task<ServiceResult> SetActiveAsync(string id, bool active, CancellationToken ct = default);
    Task<ServiceResult> UnlockAsync(string id, CancellationToken ct = default);
    Task<ServiceResult> ResetPasswordAsync(string id, AdminResetPasswordRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<RoleSummaryDto>> RolesAsync(CancellationToken ct = default);
}

/// <summary>
/// User &amp; role administration (Admin). Managers get a read-only view of their own team.
/// Every security change updates the security stamp (invalidating sessions/tokens) and is audited.
/// </summary>
public sealed class UserAdminService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    IDataScope scope,
    IRequestValidator validator,
    IAuditService audit,
    IPasswordHistoryService passwordHistory,
    IBusinessClock clock) : IUserAdminService
{
    public async Task<PagedResult<UserDto>> SearchAsync(UserQuery query, CancellationToken ct = default)
    {
        query.Normalize();
        var q = db.Users.AsNoTracking().AsQueryable();
        var visible = await scope.VisibleOwnerIdsAsync(ct);
        if (visible is not null) q = q.Where(u => visible.Contains(u.Id));

        if (query.Search is { } s)
        {
            var like = $"%{s}%";
            q = q.Where(u => EF.Functions.Like(u.FullName, like) || EF.Functions.Like(u.Email!, like) || EF.Functions.Like(u.UserName!, like));
        }
        if (!string.IsNullOrEmpty(query.Role))
        {
            var roleId = await db.Roles.Where(r => r.Name == query.Role).Select(r => r.Id).FirstOrDefaultAsync(ct);
            q = q.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
        }
        var now = DateTimeOffset.UtcNow;
        q = query.State switch
        {
            "active" => q.Where(u => u.IsActive),
            "inactive" => q.Where(u => !u.IsActive),
            _ => q
        };

        var d = query.Descending;
        q = query.Sort switch
        {
            "email" => d ? q.OrderByDescending(u => u.Email) : q.OrderBy(u => u.Email),
            "created" => d ? q.OrderByDescending(u => u.CreatedDate) : q.OrderBy(u => u.CreatedDate),
            "login" => d ? q.OrderByDescending(u => u.LastLoginDate) : q.OrderBy(u => u.LastLoginDate),
            "name" when d => q.OrderByDescending(u => u.FullName),
            _ => q.OrderBy(u => u.FullName)
        };

        // Lockout filtering happens in memory: DateTimeOffset comparisons are not portable on SQLite.
        var list = await q.Select(Projection).ToListAsync(ct);
        if (query.State == "locked") list = list.Where(u => u.LockoutEnd is { } le && le > now).ToList();
        var total = list.Count;
        var page = list.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return new PagedResult<UserDto> { Items = await WithRolesAsync(page, ct), Page = query.Page, PageSize = query.PageSize, TotalCount = total };
    }

    public async Task<ServiceResult<UserDto>> GetAsync(string id, CancellationToken ct = default)
    {
        var row = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(Projection).FirstOrDefaultAsync(ct);
        if (row is null) return ServiceResult<UserDto>.NotFound();
        var visible = await scope.VisibleOwnerIdsAsync(ct);
        if (visible is not null && !visible.Contains(id)) return ServiceResult<UserDto>.Forbidden();
        return ServiceResult<UserDto>.Ok((await WithRolesAsync([row], ct))[0]);
    }

    public async Task<ServiceResult<UserDto>> CreateAsync(UserCreateRequest request, CancellationToken ct = default)
    {
        if (!scope.IsAdmin) return ServiceResult<UserDto>.Forbidden();
        var errors = validator.Validate(request);
        await ValidateRoleAndManagerAsync(request, null, errors, ct);
        if (errors.Count > 0) return ServiceResult<UserDto>.Invalid(errors);

        var email = request.Email!.Trim().ToLowerInvariant();
        if (await users.FindByEmailAsync(email) is not null)
            return ServiceResult<UserDto>.Conflict(nameof(UserCreateRequest.Email), "A user with this email already exists.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = request.FullName!.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            IsActive = request.IsActive,
            MustChangePassword = request.MustChangePassword,
            ManagerId = request.Role == AppRoles.SalesExecutive ? Blank(request.ManagerId) : null,
            CreatedDate = clock.UtcNow,
            LockoutEnabled = true
        };
        var created = await users.CreateAsync(user, request.Password!);
        if (!created.Succeeded) return ServiceResult<UserDto>.Invalid(IdentityErrors(created, nameof(UserCreateRequest.Password)));
        await users.AddToRoleAsync(user, request.Role!);
        await passwordHistory.RecordAsync(user, ct);

        await audit.LogAsync(AuditModule.User, AuditAction.Create, entityName: "User", recordId: user.Id,
            details: $"User {user.Email} created with role {request.Role}.",
            newValue: new { user.FullName, user.Email, Role = request.Role, user.ManagerId, user.IsActive, user.MustChangePassword }, ct: ct);
        return await GetAsync(user.Id, ct);
    }

    public async Task<ServiceResult<UserDto>> UpdateAsync(string id, UserUpdateRequest request, CancellationToken ct = default)
    {
        if (!scope.IsAdmin) return ServiceResult<UserDto>.Forbidden();
        var user = await users.FindByIdAsync(id);
        if (user is null) return ServiceResult<UserDto>.NotFound();

        var errors = validator.Validate(request);
        await ValidateRoleAndManagerAsync(request, id, errors, ct);
        var currentRole = (await users.GetRolesAsync(user)).FirstOrDefault();
        var isSelf = id == scope.UserId;
        if (isSelf && request.Role != AppRoles.Admin) errors.Add(nameof(UserUpdateRequest.Role), "You cannot remove your own Admin role.");
        if (isSelf && !request.IsActive) errors.Add(nameof(UserUpdateRequest.IsActive), "You cannot deactivate your own account.");
        if (currentRole == AppRoles.Admin && (request.Role != AppRoles.Admin || !request.IsActive) && !await OtherActiveAdminExistsAsync(id))
            errors.Add(nameof(UserUpdateRequest.Role), "At least one active Admin must remain.");
        if (errors.Count > 0) return ServiceResult<UserDto>.Invalid(errors);

        var email = request.Email!.Trim().ToLowerInvariant();
        var other = await users.FindByEmailAsync(email);
        if (other is not null && other.Id != id)
            return ServiceResult<UserDto>.Conflict(nameof(UserUpdateRequest.Email), "A user with this email already exists.");

        var before = new { user.FullName, user.Email, user.PhoneNumber, Role = currentRole, user.ManagerId, user.IsActive };
        user.FullName = request.FullName!.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = email;
            user.UserName = email;
        }
        user.PhoneNumber = Blank(request.PhoneNumber);
        user.ManagerId = request.Role == AppRoles.SalesExecutive ? Blank(request.ManagerId) : null;
        var activeChanged = user.IsActive != request.IsActive;
        user.IsActive = request.IsActive;

        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) return ServiceResult<UserDto>.Invalid(IdentityErrors(updated, ""));

        var roleChanged = currentRole != request.Role;
        if (roleChanged)
        {
            if (currentRole is not null) await users.RemoveFromRoleAsync(user, currentRole);
            await users.AddToRoleAsync(user, request.Role!);
            if (currentRole == AppRoles.Manager)
            {
                // Former team members no longer report to a manager.
                await db.Users.Where(u => u.ManagerId == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ManagerId, (string?)null), ct);
            }
            await audit.LogAsync(AuditModule.Security, AuditAction.RoleChange, entityName: "User", recordId: user.Id,
                details: $"Role of {user.Email} changed from {AppRoles.DisplayName(currentRole)} to {AppRoles.DisplayName(request.Role)}.",
                oldValue: new { Role = currentRole }, newValue: new { request.Role }, ct: ct);
        }
        if (roleChanged || activeChanged) await users.UpdateSecurityStampAsync(user);
        if (activeChanged)
            await audit.LogAsync(AuditModule.Security, user.IsActive ? AuditAction.Activate : AuditAction.Deactivate, entityName: "User", recordId: user.Id,
                details: $"User {user.Email} {(user.IsActive ? "activated" : "deactivated")}.", ct: ct);

        await audit.LogAsync(AuditModule.User, AuditAction.Update, entityName: "User", recordId: user.Id, details: $"User {user.Email} updated.",
            oldValue: before, newValue: new { user.FullName, user.Email, user.PhoneNumber, request.Role, user.ManagerId, user.IsActive }, ct: ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult> SetActiveAsync(string id, bool active, CancellationToken ct = default)
    {
        if (!scope.IsAdmin) return ServiceResult.Forbidden();
        var user = await users.FindByIdAsync(id);
        if (user is null) return ServiceResult.NotFound();
        if (user.IsActive == active) return ServiceResult.Ok();
        if (!active && id == scope.UserId) return ServiceResult.Invalid("", "You cannot deactivate your own account.");
        if (!active && await users.IsInRoleAsync(user, AppRoles.Admin) && !await OtherActiveAdminExistsAsync(id))
            return ServiceResult.Invalid("", "At least one active Admin must remain.");

        user.IsActive = active;
        await users.UpdateAsync(user);
        await users.UpdateSecurityStampAsync(user); // terminates existing sessions and API tokens
        await audit.LogAsync(AuditModule.Security, active ? AuditAction.Activate : AuditAction.Deactivate, entityName: "User", recordId: id,
            details: $"User {user.Email} {(active ? "activated" : "deactivated")}.", ct: ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UnlockAsync(string id, CancellationToken ct = default)
    {
        if (!scope.IsAdmin) return ServiceResult.Forbidden();
        var user = await users.FindByIdAsync(id);
        if (user is null) return ServiceResult.NotFound();
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await audit.LogAsync(AuditModule.Security, AuditAction.Unlock, entityName: "User", recordId: id,
            details: $"Account {user.Email} unlocked by administrator.", ct: ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ResetPasswordAsync(string id, AdminResetPasswordRequest request, CancellationToken ct = default)
    {
        if (!scope.IsAdmin) return ServiceResult.Forbidden();
        var user = await users.FindByIdAsync(id);
        if (user is null) return ServiceResult.NotFound();
        var errors = validator.Validate(request);
        if (errors.Count > 0) return ServiceResult.Invalid(errors);

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, request.NewPassword!);
        if (!result.Succeeded) return ServiceResult.Invalid(IdentityErrors(result, nameof(AdminResetPasswordRequest.NewPassword)));

        user.MustChangePassword = request.RequireChange;
        await users.UpdateAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await passwordHistory.RecordAsync(user, ct);
        await audit.LogAsync(AuditModule.Security, AuditAction.PasswordReset, entityName: "User", recordId: id,
            details: $"Password reset by administrator for {user.Email}." + (request.RequireChange ? " Change required at next sign-in." : ""), ct: ct);
        return ServiceResult.Ok();
    }

    public async Task<IReadOnlyList<RoleSummaryDto>> RolesAsync(CancellationToken ct = default)
    {
        var counts = await (from ur in db.UserRoles
                            join r in db.Roles on ur.RoleId equals r.Id
                            join u in db.Users on ur.UserId equals u.Id
                            group u by r.Name into g
                            select new { Role = g.Key, Total = g.Count(), Active = g.Count(x => x.IsActive) }).ToListAsync(ct);
        return AppRoles.All.Select(r =>
        {
            var c = counts.FirstOrDefault(x => x.Role == r);
            return new RoleSummaryDto(r, AppRoles.DisplayName(r), AppRoles.Description(r), c?.Total ?? 0, c?.Active ?? 0);
        }).ToList();
    }

    private async Task ValidateRoleAndManagerAsync(UserUpdateRequest r, string? userId, Dictionary<string, List<string>> errors, CancellationToken ct)
    {
        if (r.Role is not null && !AppRoles.All.Contains(r.Role))
            errors.Add(nameof(UserUpdateRequest.Role), "Select a valid role.");
        if (r.Role == AppRoles.SalesExecutive && Blank(r.ManagerId) is { } managerId)
        {
            if (managerId == userId) errors.Add(nameof(UserUpdateRequest.ManagerId), "A user cannot report to themselves.");
            var manager = await users.FindByIdAsync(managerId);
            if (manager is null || !manager.IsActive || !await users.IsInRoleAsync(manager, AppRoles.Manager))
                errors.Add(nameof(UserUpdateRequest.ManagerId), "Select an active Manager.");
        }
    }

    private async Task<bool> OtherActiveAdminExistsAsync(string excludingId) =>
        (await users.GetUsersInRoleAsync(AppRoles.Admin)).Any(u => u.Id != excludingId && u.IsActive);

    private async Task<List<UserDto>> WithRolesAsync(List<UserDto> list, CancellationToken ct)
    {
        var ids = list.Select(u => u.Id).ToList();
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ids.Contains(ur.UserId) select new { ur.UserId, r.Name })
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        return list.Select(u => new UserDto
        {
            Id = u.Id, FullName = u.FullName, Email = u.Email, UserName = u.UserName, PhoneNumber = u.PhoneNumber,
            Role = roles.FirstOrDefault(r => r.UserId == u.Id)?.Name, IsActive = u.IsActive,
            IsLockedOut = u.LockoutEnd is { } le && le > now, LockoutEnd = u.LockoutEnd, FailedLoginCount = u.FailedLoginCount,
            MustChangePassword = u.MustChangePassword, ManagerId = u.ManagerId, ManagerName = u.ManagerName,
            CreatedDate = u.CreatedDate, LastLoginDate = u.LastLoginDate
        }).ToList();
    }

    private static readonly System.Linq.Expressions.Expression<Func<ApplicationUser, UserDto>> Projection = u => new UserDto
    {
        Id = u.Id,
        FullName = u.FullName,
        Email = u.Email ?? "",
        UserName = u.UserName ?? "",
        PhoneNumber = u.PhoneNumber,
        IsActive = u.IsActive,
        LockoutEnd = u.LockoutEnd,
        FailedLoginCount = u.AccessFailedCount,
        MustChangePassword = u.MustChangePassword,
        ManagerId = u.ManagerId,
        ManagerName = u.Manager != null ? u.Manager.FullName : null,
        CreatedDate = u.CreatedDate,
        LastLoginDate = u.LastLoginDate
    };

    private static Dictionary<string, List<string>> IdentityErrors(IdentityResult result, string field)
    {
        var errors = new Dictionary<string, List<string>>();
        foreach (var e in result.Errors)
        {
            var key = e.Code.Contains("Email", StringComparison.OrdinalIgnoreCase) || e.Code.Contains("UserName", StringComparison.OrdinalIgnoreCase)
                ? "Email" : field;
            errors.Add(key, e.Description);
        }
        return errors;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
