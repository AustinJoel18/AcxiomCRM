using System.Text.Json;
using System.Text.Json.Serialization;
using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Web.Services;

/// <summary>
/// Explicit audit entries for security events (login, lockout, role change, password reset...).
/// CRM record create/update/delete is audited automatically by <see cref="ApplicationDbContext"/>.
/// Never pass passwords, hashes or tokens in <c>details</c>/values.
/// </summary>
public interface IAuditService
{
    Task LogAsync(AuditModule module, AuditAction action, AuditResult result = AuditResult.Success,
        string? entityName = null, string? recordId = null, string? details = null,
        object? oldValue = null, object? newValue = null,
        string? userId = null, string? userName = null, CancellationToken ct = default);

    Task<PagedResult<AuditLogDto>> SearchAsync(AuditQuery query, CancellationToken ct = default);
    IQueryable<AuditLog> ScopedQuery(IReadOnlyList<string>? visibleUserIds, bool limited);
    Task<AuditLogDto?> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogDto>> ForRecordAsync(string entityName, string recordId, int take = 50, CancellationToken ct = default);
}

public sealed class AuditService(ApplicationDbContext db, ICurrentUser currentUser, IBusinessClock clock, IDataScope scope) : IAuditService
{
    /// <summary>Modules hidden from the Manager's limited audit view.</summary>
    public static readonly AuditModule[] SecurityModules = [AuditModule.Authentication, AuditModule.Security, AuditModule.User];

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public async Task LogAsync(AuditModule module, AuditAction action, AuditResult result = AuditResult.Success,
        string? entityName = null, string? recordId = null, string? details = null,
        object? oldValue = null, object? newValue = null,
        string? userId = null, string? userName = null, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            CreatedDate = clock.UtcNow,
            UserId = userId ?? currentUser.UserId,
            UserName = Truncate(userName ?? currentUser.UserName, 256),
            Module = module,
            Action = action,
            Result = result,
            EntityName = entityName,
            RecordId = recordId,
            Details = Truncate(details, 1000),
            OldValue = oldValue is null ? null : JsonSerializer.Serialize(oldValue, Json),
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue, Json),
            IpAddress = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent
        });
        await db.SaveChangesAsync(ct);
    }

    public IQueryable<AuditLog> ScopedQuery(IReadOnlyList<string>? visibleUserIds, bool limited)
    {
        var q = db.AuditLogs.AsNoTracking();
        if (visibleUserIds is not null) q = q.Where(a => a.UserId != null && visibleUserIds.Contains(a.UserId));
        if (limited) q = q.Where(a => !SecurityModules.Contains(a.Module));
        return q;
    }

    public async Task<PagedResult<AuditLogDto>> SearchAsync(AuditQuery query, CancellationToken ct = default)
    {
        query.Normalize();
        var q = await BaseQueryAsync(ct);

        if (!string.IsNullOrEmpty(query.UserId)) q = q.Where(a => a.UserId == query.UserId);
        if (query.Module is { } m) q = q.Where(a => a.Module == m);
        if (query.Action is { } ac) q = q.Where(a => a.Action == ac);
        if (query.Result is { } r) q = q.Where(a => a.Result == r);
        if (!string.IsNullOrEmpty(query.EntityName)) q = q.Where(a => a.EntityName == query.EntityName);
        if (!string.IsNullOrEmpty(query.RecordId)) q = q.Where(a => a.RecordId == query.RecordId);
        if (query.DateFrom is { } from) { var f = clock.ToUtc(from.Date); q = q.Where(a => a.CreatedDate >= f); }
        if (query.DateTo is { } to) { var t = clock.ToUtc(to.Date.AddDays(1)); q = q.Where(a => a.CreatedDate < t); }
        if (query.Search is { } s)
            q = q.Where(a => (a.UserName != null && a.UserName.Contains(s)) || (a.Details != null && a.Details.Contains(s))
                             || (a.RecordId != null && a.RecordId == s) || (a.IpAddress != null && a.IpAddress.Contains(s)));

        q = query.Sort switch
        {
            "user" => query.Descending ? q.OrderByDescending(a => a.UserName) : q.OrderBy(a => a.UserName),
            "module" => query.Descending ? q.OrderByDescending(a => a.Module) : q.OrderBy(a => a.Module),
            "action" => query.Descending ? q.OrderByDescending(a => a.Action) : q.OrderBy(a => a.Action),
            "date" when !query.Descending => q.OrderBy(a => a.AuditLogId),
            _ => q.OrderByDescending(a => a.AuditLogId)
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(Projection).ToListAsync(ct);
        return new PagedResult<AuditLogDto> { Items = items, Page = query.Page, PageSize = query.PageSize, TotalCount = total };
    }

    public async Task<AuditLogDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var q = await BaseQueryAsync(ct);
        return await q.Where(a => a.AuditLogId == id).Select(Projection).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLogDto>> ForRecordAsync(string entityName, string recordId, int take = 50, CancellationToken ct = default) =>
        await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == entityName && a.RecordId == recordId)
            .OrderByDescending(a => a.AuditLogId).Take(take).Select(Projection).ToListAsync(ct);

    private async Task<IQueryable<AuditLog>> BaseQueryAsync(CancellationToken ct)
    {
        if (scope.IsAdmin) return ScopedQuery(null, limited: false);
        var visible = await scope.VisibleOwnerIdsAsync(ct) ?? [];
        return ScopedQuery(visible, limited: true);
    }

    private static readonly System.Linq.Expressions.Expression<Func<AuditLog, AuditLogDto>> Projection = a => new AuditLogDto
    {
        AuditLogId = a.AuditLogId,
        CreatedDate = a.CreatedDate,
        UserId = a.UserId,
        UserName = a.UserName,
        Module = a.Module,
        Action = a.Action,
        EntityName = a.EntityName,
        RecordId = a.RecordId,
        Result = a.Result,
        OldValue = a.OldValue,
        NewValue = a.NewValue,
        Details = a.Details,
        IpAddress = a.IpAddress,
        UserAgent = a.UserAgent
    };

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
