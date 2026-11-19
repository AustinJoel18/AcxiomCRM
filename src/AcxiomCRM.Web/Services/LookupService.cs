using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Domain;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Web.Services;

/// <summary>Scoped option lists for dropdowns (owners, customers, leads, opportunities).</summary>
public interface ILookupService
{
    /// <summary>Active users the caller may assign records to; <paramref name="includeUserId"/> (the current owner) is always listed.</summary>
    Task<IReadOnlyList<OptionDto>> AssignableUsersAsync(string? includeUserId = null, CancellationToken ct = default);
    Task<IReadOnlyList<OptionDto>> VisibleUsersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OptionDto>> CustomersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OptionDto>> LeadsAsync(bool openOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<OptionDto>> RelatedRecordsAsync(bool includeOpportunities = true, CancellationToken ct = default);
    Task<IReadOnlyList<OptionDto>> ManagersAsync(CancellationToken ct = default);
    Task<RelatedRecordDto?> ResolveRelatedAsync(RelatedRecordType type, int id, CancellationToken ct = default);
}

public sealed class LookupService(ApplicationDbContext db, IDataScope scope) : ILookupService
{
    private const int Limit = 500;

    public async Task<IReadOnlyList<OptionDto>> AssignableUsersAsync(string? includeUserId = null, CancellationToken ct = default)
    {
        var ids = await scope.VisibleOwnerIdsAsync(ct);
        var q = db.Users.AsNoTracking().Where(u => u.IsActive);
        if (ids is not null) q = q.Where(u => ids.Contains(u.Id));
        var list = await q.OrderBy(u => u.FullName).Select(u => new OptionDto(u.Id, u.FullName)).ToListAsync(ct);
        if (!string.IsNullOrEmpty(includeUserId) && list.All(o => o.Value != includeUserId))
        {
            var name = await db.Users.AsNoTracking().Where(u => u.Id == includeUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
            if (name is not null) list.Insert(0, new OptionDto(includeUserId, name + " (current owner)"));
        }
        return list;
    }

    public async Task<IReadOnlyList<OptionDto>> VisibleUsersAsync(CancellationToken ct = default)
    {
        var ids = await scope.VisibleOwnerIdsAsync(ct);
        var q = db.Users.AsNoTracking();
        if (ids is not null) q = q.Where(u => ids.Contains(u.Id));
        return await q.OrderBy(u => u.FullName)
            .Select(u => new OptionDto(u.Id, u.IsActive ? u.FullName : u.FullName + " (inactive)")).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OptionDto>> CustomersAsync(CancellationToken ct = default)
    {
        var q = await scope.ApplyAsync(db.Customers.AsNoTracking(), ct);
        return await q.OrderBy(c => c.CustomerName).Take(Limit)
            .Select(c => new OptionDto(c.CustomerId.ToString(), c.CustomerName + (c.CompanyName != null ? " — " + c.CompanyName : ""), null))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OptionDto>> LeadsAsync(bool openOnly = false, CancellationToken ct = default)
    {
        var q = await scope.ApplyAsync(db.Leads.AsNoTracking(), ct);
        if (openOnly) q = q.Where(l => LeadWorkflow.OpenStatuses.Contains(l.Status));
        return await q.OrderBy(l => l.LeadName).Take(Limit)
            .Select(l => new OptionDto(l.LeadId.ToString(), l.LeadName + " (" + l.LeadCode + ")", null)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OptionDto>> RelatedRecordsAsync(bool includeOpportunities = true, CancellationToken ct = default)
    {
        var result = new List<OptionDto>();
        foreach (var c in await CustomersAsync(ct)) result.Add(new OptionDto($"Customer:{c.Value}", c.Text, "Customers"));
        foreach (var l in await LeadsAsync(openOnly: false, ct)) result.Add(new OptionDto($"Lead:{l.Value}", l.Text, "Leads"));
        if (includeOpportunities)
        {
            var q = await scope.ApplyAsync(db.Opportunities.AsNoTracking(), ct);
            var opps = await q.Where(o => o.Status == OpportunityStatus.Open).OrderBy(o => o.OpportunityName).Take(Limit)
                .Select(o => new { o.OpportunityId, o.OpportunityName }).ToListAsync(ct);
            result.AddRange(opps.Select(o => new OptionDto($"Opportunity:{o.OpportunityId}", o.OpportunityName, "Opportunities")));
        }
        return result;
    }

    public async Task<IReadOnlyList<OptionDto>> ManagersAsync(CancellationToken ct = default)
    {
        var managerRoleId = await db.Roles.Where(r => r.Name == AppRoles.Manager).Select(r => r.Id).FirstOrDefaultAsync(ct);
        return await db.Users.AsNoTracking()
            .Where(u => u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == managerRoleId))
            .OrderBy(u => u.FullName).Select(u => new OptionDto(u.Id, u.FullName)).ToListAsync(ct);
    }

    /// <summary>Resolves a related record only if it exists and is inside the caller's scope.</summary>
    public async Task<RelatedRecordDto?> ResolveRelatedAsync(RelatedRecordType type, int id, CancellationToken ct = default)
    {
        switch (type)
        {
            case RelatedRecordType.Customer:
                var cq = await scope.ApplyAsync(db.Customers.AsNoTracking(), ct);
                return await cq.Where(c => c.CustomerId == id).Select(c => new RelatedRecordDto(type, c.CustomerId, c.CustomerName)).FirstOrDefaultAsync(ct);
            case RelatedRecordType.Lead:
                var lq = await scope.ApplyAsync(db.Leads.AsNoTracking(), ct);
                return await lq.Where(l => l.LeadId == id).Select(l => new RelatedRecordDto(type, l.LeadId, l.LeadName)).FirstOrDefaultAsync(ct);
            case RelatedRecordType.Opportunity:
                var oq = await scope.ApplyAsync(db.Opportunities.AsNoTracking(), ct);
                return await oq.Where(o => o.OpportunityId == id).Select(o => new RelatedRecordDto(type, o.OpportunityId, o.OpportunityName)).FirstOrDefaultAsync(ct);
            default:
                return null;
        }
    }
}
