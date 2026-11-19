using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Validation;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Web.Services;

public interface IActivityService
{
    Task<PagedResult<ActivityDto>> SearchAsync(ActivityQuery query, RelatedRecordType? relatedType = null, int? relatedId = null, CancellationToken ct = default);
    Task<ServiceResult<ActivityDto>> GetAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<ActivityDto>> CreateAsync(ActivitySaveRequest request, CancellationToken ct = default);
    Task<ServiceResult<ActivityDto>> UpdateAsync(int id, ActivitySaveRequest request, CancellationToken ct = default);
    Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class ActivityService(ApplicationDbContext db, IDataScope scope, IRequestValidator validator, IBusinessClock clock, ILookupService lookups)
    : CrmServiceBase(db, scope, validator, clock), IActivityService
{
    private static readonly System.Linq.Expressions.Expression<Func<Activity, ActivityDto>> Projection = a => new ActivityDto
    {
        ActivityId = a.ActivityId,
        ActivityType = a.ActivityType,
        Subject = a.Subject,
        Description = a.Description,
        ActivityDate = a.ActivityDate,
        Status = a.Status,
        RelatedType = a.CustomerId != null ? RelatedRecordType.Customer : a.LeadId != null ? RelatedRecordType.Lead
            : a.OpportunityId != null ? RelatedRecordType.Opportunity : null,
        RelatedId = a.CustomerId ?? a.LeadId ?? a.OpportunityId,
        RelatedName = a.Customer != null ? a.Customer.CustomerName : a.Lead != null ? a.Lead.LeadName
            : a.Opportunity != null ? a.Opportunity.OpportunityName : null,
        FollowUpId = a.FollowUpId,
        AssignedToId = a.AssignedToId,
        AssignedToName = a.AssignedTo != null ? a.AssignedTo.FullName : null,
        CreatedDate = a.CreatedDate
    };

    public async Task<PagedResult<ActivityDto>> SearchAsync(ActivityQuery query, RelatedRecordType? relatedType = null, int? relatedId = null, CancellationToken ct = default)
    {
        query.Normalize();
        var q = await Scope.ApplyAsync(Db.Activities.AsNoTracking(), ct);
        if (query.Search is { } s)
        {
            var like = $"%{s}%";
            q = q.Where(a => EF.Functions.Like(a.Subject, like) || (a.Description != null && EF.Functions.Like(a.Description, like)));
        }
        if (query.Type is { } t) q = q.Where(a => a.ActivityType == t);
        if (query.Status is { } st) q = q.Where(a => a.Status == st);
        if (!string.IsNullOrEmpty(query.AssignedToId)) q = q.Where(a => a.AssignedToId == query.AssignedToId);
        if (query.DateFrom is { } from) { var fd = from.Date; q = q.Where(a => a.ActivityDate >= fd); }
        if (query.DateTo is { } to) { var td = to.Date.AddDays(1); q = q.Where(a => a.ActivityDate < td); }
        if (relatedType is { } rt && relatedId is { } rid)
        {
            q = rt switch
            {
                RelatedRecordType.Customer => q.Where(a => a.CustomerId == rid),
                RelatedRecordType.Lead => q.Where(a => a.LeadId == rid),
                _ => q.Where(a => a.OpportunityId == rid)
            };
        }

        var d = query.Descending;
        q = query.Sort switch
        {
            "type" => d ? q.OrderByDescending(a => a.ActivityType) : q.OrderBy(a => a.ActivityType),
            "subject" => d ? q.OrderByDescending(a => a.Subject) : q.OrderBy(a => a.Subject),
            "status" => d ? q.OrderByDescending(a => a.Status) : q.OrderBy(a => a.Status),
            "owner" => d ? q.OrderByDescending(a => a.AssignedTo!.FullName) : q.OrderBy(a => a.AssignedTo!.FullName),
            "date" when !d => q.OrderBy(a => a.ActivityDate),
            _ => q.OrderByDescending(a => a.ActivityDate)
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(Projection).ToListAsync(ct);
        return new PagedResult<ActivityDto> { Items = items, Page = query.Page, PageSize = query.PageSize, TotalCount = total };
    }

    public async Task<ServiceResult<ActivityDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var owner = await Db.Activities.AsNoTracking().Where(a => a.ActivityId == id).Select(a => a.AssignedToId).FirstOrDefaultAsync(ct);
        if (owner is null) return ServiceResult<ActivityDto>.NotFound();
        var ids = await Scope.VisibleOwnerIdsAsync(ct);
        if (ids is not null && !ids.Contains(owner)) return ServiceResult<ActivityDto>.Forbidden();
        return ServiceResult<ActivityDto>.Ok(await Db.Activities.AsNoTracking().Where(a => a.ActivityId == id).Select(Projection).FirstAsync(ct));
    }

    public async Task<ServiceResult<ActivityDto>> CreateAsync(ActivitySaveRequest request, CancellationToken ct = default)
    {
        var errors = Validator.Validate(request);
        var related = await ResolveRelatedAsync(request, errors, ct);
        var (assignee, assignError) = await ResolveAssigneeAsync(request.AssignedToId, null, ct);
        if (assignError is not null) errors.Add(nameof(ActivitySaveRequest.AssignedToId), assignError);
        if (errors.Count > 0) return ServiceResult<ActivityDto>.Invalid(errors);

        var a = new Activity { AssignedToId = assignee! };
        Apply(a, request, related);
        Db.Activities.Add(a);
        await Db.SaveChangesAsync(ct);
        return await GetAsync(a.ActivityId, ct);
    }

    public async Task<ServiceResult<ActivityDto>> UpdateAsync(int id, ActivitySaveRequest request, CancellationToken ct = default)
    {
        var a = await Db.Activities.FirstOrDefaultAsync(x => x.ActivityId == id, ct);
        if (a is null) return ServiceResult<ActivityDto>.NotFound();
        if (!await Scope.CanAccessAsync(a, ct)) return ServiceResult<ActivityDto>.Forbidden();

        var errors = Validator.Validate(request);
        var related = await ResolveRelatedAsync(request, errors, ct);
        var (assignee, assignError) = await ResolveAssigneeAsync(request.AssignedToId, a.AssignedToId, ct);
        if (assignError is not null) errors.Add(nameof(ActivitySaveRequest.AssignedToId), assignError);
        if (errors.Count > 0) return ServiceResult<ActivityDto>.Invalid(errors);

        var oldStatus = a.Status;
        Apply(a, request, related);
        a.AssignedToId = assignee!;
        if (oldStatus != a.Status) Db.SetAuditHint(a, AuditAction.StatusChange, $"Activity status changed from {oldStatus} to {a.Status}.");
        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var a = await Db.Activities.FirstOrDefaultAsync(x => x.ActivityId == id, ct);
        if (a is null) return ServiceResult.NotFound();
        if (!await Scope.CanAccessAsync(a, ct)) return ServiceResult.Forbidden();
        if (!Scope.CanDelete && a.AssignedToId != Scope.UserId) return ServiceResult.Forbidden();
        Db.Activities.Remove(a);
        await Db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    private async Task<RelatedRecordDto?> ResolveRelatedAsync(ActivitySaveRequest r, Dictionary<string, List<string>> errors, CancellationToken ct)
    {
        if (r.RelatedType is null || r.RelatedId is null) return null; // activities may be stand-alone tasks
        var related = await lookups.ResolveRelatedAsync(r.RelatedType.Value, r.RelatedId.Value, ct);
        if (related is null) errors.Add(nameof(ActivitySaveRequest.RelatedId), "Select a valid related record.");
        return related;
    }

    private static void Apply(Activity a, ActivitySaveRequest r, RelatedRecordDto? related)
    {
        a.ActivityType = r.ActivityType!.Value;
        a.Subject = r.Subject!.Trim();
        a.Description = Clean(r.Description);
        a.ActivityDate = r.ActivityDate!.Value;
        a.Status = r.Status!.Value;
        a.CustomerId = related?.Type == RelatedRecordType.Customer ? related.Id : null;
        a.LeadId = related?.Type == RelatedRecordType.Lead ? related.Id : null;
        a.OpportunityId = related?.Type == RelatedRecordType.Opportunity ? related.Id : null;
    }
}
