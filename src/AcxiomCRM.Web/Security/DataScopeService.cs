using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Web.Security;

/// <summary>
/// Resource-level (ownership) authorization. Determines which owners' records the current user may see:
/// Admin → everything; Manager → own + direct reports (team); Sales Executive → own/assigned only.
/// Every list query and every single-record read/write goes through this service, so tampered IDs
/// in URLs or API payloads cannot reach out-of-scope data.
/// </summary>
public interface IDataScope
{
    string UserId { get; }
    bool IsAdmin { get; }
    bool IsManager { get; }
    bool IsSalesExecutive { get; }
    bool CanDelete { get; }

    /// <summary>"All records", "Team" or "Own/Assigned".</summary>
    string Description { get; }

    /// <summary>Owner IDs visible to the caller, or null when unrestricted (Admin).</summary>
    Task<IReadOnlyList<string>?> VisibleOwnerIdsAsync(CancellationToken ct = default);

    Task<IQueryable<T>> ApplyAsync<T>(IQueryable<T> query, CancellationToken ct = default) where T : class, IOwnedEntity;

    Task<bool> CanAccessAsync(IOwnedEntity entity, CancellationToken ct = default);

    /// <summary>Assignee must be an active user inside the caller's scope.</summary>
    Task<bool> CanAssignToAsync(string assigneeId, CancellationToken ct = default);
}

public sealed class DataScopeService(ApplicationDbContext db, ICurrentUser currentUser) : IDataScope
{
    private IReadOnlyList<string>? _visible;
    private bool _loaded;

    public string UserId => currentUser.UserId ?? throw new InvalidOperationException("No authenticated user.");
    public bool IsAdmin => currentUser.IsInRole(AppRoles.Admin);
    public bool IsManager => !IsAdmin && currentUser.IsInRole(AppRoles.Manager);
    public bool IsSalesExecutive => !IsAdmin && !IsManager && currentUser.IsInRole(AppRoles.SalesExecutive);
    public bool CanDelete => IsAdmin || IsManager;

    public string Description => IsAdmin ? "All records" : IsManager ? "Team" : "Own/Assigned";

    public async Task<IReadOnlyList<string>?> VisibleOwnerIdsAsync(CancellationToken ct = default)
    {
        if (_loaded) return _visible;
        if (IsAdmin)
        {
            _visible = null;
        }
        else if (IsManager)
        {
            var me = UserId;
            var team = await db.Users.Where(u => u.ManagerId == me).Select(u => u.Id).ToListAsync(ct);
            team.Add(me);
            _visible = team;
        }
        else if (IsSalesExecutive)
        {
            _visible = [UserId];
        }
        else
        {
            _visible = []; // authenticated but no CRM role: sees nothing
        }
        _loaded = true;
        return _visible;
    }

    public async Task<IQueryable<T>> ApplyAsync<T>(IQueryable<T> query, CancellationToken ct = default) where T : class, IOwnedEntity
    {
        var ids = await VisibleOwnerIdsAsync(ct);
        return ids is null ? query : query.Where(e => ids.Contains(EF.Property<string>(e, nameof(IOwnedEntity.AssignedToId))));
    }

    public async Task<bool> CanAccessAsync(IOwnedEntity entity, CancellationToken ct = default)
    {
        var ids = await VisibleOwnerIdsAsync(ct);
        return ids is null || ids.Contains(entity.AssignedToId);
    }

    public async Task<bool> CanAssignToAsync(string assigneeId, CancellationToken ct = default)
    {
        var ids = await VisibleOwnerIdsAsync(ct);
        if (ids is not null && !ids.Contains(assigneeId)) return false;
        return await db.Users.AnyAsync(u => u.Id == assigneeId && u.IsActive, ct);
    }
}
