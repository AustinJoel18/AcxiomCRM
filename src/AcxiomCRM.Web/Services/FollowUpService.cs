using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Domain;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Validation;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Web.Services;

public interface IFollowUpService
{
    Task<PagedResult<FollowUpDto>> SearchAsync(FollowUpQuery query, CancellationToken ct = default);
    Task<ServiceResult<FollowUpDto>> GetAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<FollowUpDto>> CreateAsync(FollowUpCreateRequest request, CancellationToken ct = default);
    Task<ServiceResult<FollowUpDto>> UpdateAsync(int id, FollowUpEditRequest request, CancellationToken ct = default);
    Task<ServiceResult<FollowUpDto>> CompleteAsync(int id, FollowUpCompleteRequest request, CancellationToken ct = default);
    Task<ServiceResult<FollowUpDto>> RescheduleAsync(int id, FollowUpRescheduleRequest request, CancellationToken ct = default);
    Task<ServiceResult<FollowUpDto>> MarkMissedAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<FollowUpDto>> CancelAsync(int id, CancellationToken ct = default);
    Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default);
    Task<(int Overdue, int DueToday, IReadOnlyList<FollowUpDto> Items)> RemindersForCurrentUserAsync(CancellationToken ct = default);
}

public sealed class FollowUpService(ApplicationDbContext db, IDataScope scope, IRequestValidator validator, IBusinessClock clock, ILookupService lookups)
    : CrmServiceBase(db, scope, validator, clock), IFollowUpService
{
    public async Task<PagedResult<FollowUpDto>> SearchAsync(FollowUpQuery query, CancellationToken ct = default)
    {
        query.Normalize();
        var q = await Scope.ApplyAsync(Db.FollowUps.AsNoTracking(), ct);
        var now = Clock.Now;
        var today = Clock.Today;
        var tomorrow = today.AddDays(1);

        q = query.View switch
        {
            "pending" => q.Where(f => f.Status == FollowUpStatus.Planned),
            "overdue" => q.Where(f => f.Status == FollowUpStatus.Planned && f.FollowUpDate < now),
            "today" => q.Where(f => f.Status == FollowUpStatus.Planned && f.FollowUpDate >= today && f.FollowUpDate < tomorrow),
            "upcoming" => q.Where(f => f.Status == FollowUpStatus.Planned && f.FollowUpDate >= now),
            _ => q
        };
        if (query.Search is { } s)
        {
            var like = $"%{s}%";
            q = q.Where(f => EF.Functions.Like(f.Subject, like) || (f.Remarks != null && EF.Functions.Like(f.Remarks, like))
                             || (f.Customer != null && EF.Functions.Like(f.Customer.CustomerName, like))
                             || (f.Lead != null && EF.Functions.Like(f.Lead.LeadName, like))
                             || (f.Opportunity != null && EF.Functions.Like(f.Opportunity.OpportunityName, like)));
        }
        if (query.Status is { } st) q = q.Where(f => f.Status == st);
        if (!string.IsNullOrEmpty(query.AssignedToId)) q = q.Where(f => f.AssignedToId == query.AssignedToId);
        if (query.DateFrom is { } from) { var fd = from.Date; q = q.Where(f => f.FollowUpDate >= fd); }
        if (query.DateTo is { } to) { var td = to.Date.AddDays(1); q = q.Where(f => f.FollowUpDate < td); }
        if (query.RelatedType is { } rt && query.RelatedId is { } rid)
        {
            q = rt switch
            {
                RelatedRecordType.Customer => q.Where(f => f.CustomerId == rid),
                RelatedRecordType.Lead => q.Where(f => f.LeadId == rid),
                _ => q.Where(f => f.OpportunityId == rid)
            };
        }

        var d = query.Descending;
        q = query.Sort switch
        {
            "subject" => d ? q.OrderByDescending(f => f.Subject) : q.OrderBy(f => f.Subject),
            "type" => d ? q.OrderByDescending(f => f.FollowUpType) : q.OrderBy(f => f.FollowUpType),
            "status" => d ? q.OrderByDescending(f => f.Status) : q.OrderBy(f => f.Status),
            "owner" => d ? q.OrderByDescending(f => f.AssignedTo!.FullName) : q.OrderBy(f => f.AssignedTo!.FullName),
            "date" => d ? q.OrderByDescending(f => f.FollowUpDate) : q.OrderBy(f => f.FollowUpDate),
            _ => query.View is "pending" or "overdue" or "today" or "upcoming"
                ? q.OrderBy(f => f.FollowUpDate)
                : q.OrderByDescending(f => f.FollowUpDate)
        };

        var total = await q.CountAsync(ct);
        var rows = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(Row).ToListAsync(ct);
        return new PagedResult<FollowUpDto> { Items = rows.Select(r => ToDto(r, now)).ToList(), Page = query.Page, PageSize = query.PageSize, TotalCount = total };
    }

    public async Task<ServiceResult<FollowUpDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var owner = await Db.FollowUps.AsNoTracking().Where(f => f.FollowUpId == id).Select(f => f.AssignedToId).FirstOrDefaultAsync(ct);
        if (owner is null) return ServiceResult<FollowUpDto>.NotFound();
        var ids = await Scope.VisibleOwnerIdsAsync(ct);
        if (ids is not null && !ids.Contains(owner)) return ServiceResult<FollowUpDto>.Forbidden();
        var row = await Db.FollowUps.AsNoTracking().Where(f => f.FollowUpId == id).Select(Row).FirstAsync(ct);
        return ServiceResult<FollowUpDto>.Ok(ToDto(row, Clock.Now));
    }

    public async Task<ServiceResult<FollowUpDto>> CreateAsync(FollowUpCreateRequest request, CancellationToken ct = default)
    {
        var errors = Validator.Validate(request);
        if (request.FollowUpDate is { } date)
            errors.Merge(FollowUpRules.ValidateDate(date, Clock.Today).Errors);
        var related = await ResolveRelatedAsync(request, errors, required: true, ct);
        var (assignee, assignError) = await ResolveAssigneeAsync(request.AssignedToId, null, ct);
        if (assignError is not null) errors.Add(nameof(FollowUpEditRequest.AssignedToId), assignError);
        if (errors.Count > 0) return ServiceResult<FollowUpDto>.Invalid(errors);

        var f = new FollowUp
        {
            Subject = request.Subject!.Trim(),
            FollowUpType = request.FollowUpType!.Value,
            FollowUpDate = request.FollowUpDate!.Value,
            Status = FollowUpStatus.Planned,
            Remarks = Clean(request.Remarks),
            AssignedToId = assignee!
        };
        SetRelated(f, related);
        Db.FollowUps.Add(f);
        await Db.SaveChangesAsync(ct);
        return await GetAsync(f.FollowUpId, ct);
    }

    public async Task<ServiceResult<FollowUpDto>> UpdateAsync(int id, FollowUpEditRequest request, CancellationToken ct = default)
    {
        var (f, failure) = await LoadAsync(id, ct);
        if (failure is not null) return failure;
        if (f!.Status is FollowUpStatus.Completed or FollowUpStatus.Cancelled)
            return ServiceResult<FollowUpDto>.Invalid("", $"A {f.Status} follow-up cannot be edited.");

        var errors = Validator.Validate(request);
        var related = await ResolveRelatedAsync(request, errors, required: true, ct);
        var (assignee, assignError) = await ResolveAssigneeAsync(request.AssignedToId, f.AssignedToId, ct);
        if (assignError is not null) errors.Add(nameof(FollowUpEditRequest.AssignedToId), assignError);
        if (errors.Count > 0) return ServiceResult<FollowUpDto>.Invalid(errors);

        var oldOwner = f.AssignedToId;
        f.Subject = request.Subject!.Trim();
        f.FollowUpType = request.FollowUpType!.Value;
        f.Remarks = Clean(request.Remarks);
        f.AssignedToId = assignee!;
        SetRelated(f, related);
        if (oldOwner != f.AssignedToId) Db.SetAuditHint(f, AuditAction.Assign, "Follow-up re-assigned.");
        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult<FollowUpDto>> CompleteAsync(int id, FollowUpCompleteRequest request, CancellationToken ct = default)
    {
        var (f, failure) = await LoadAsync(id, ct);
        if (failure is not null) return failure;
        if (!FollowUpRules.CanComplete(f!.Status))
            return ServiceResult<FollowUpDto>.Invalid("", $"Only planned follow-ups can be completed (current status: {f.Status}).");
        var errors = Validator.Validate(request);
        if (errors.Count > 0) return ServiceResult<FollowUpDto>.Invalid(errors);

        f.Status = FollowUpStatus.Completed;
        f.CompletedDate = Clock.UtcNow;
        f.Outcome = request.Outcome!.Trim();
        Db.SetAuditHint(f, AuditAction.Complete, $"Follow-up completed. Outcome: {f.Outcome}");

        // Activity history for the related record.
        Db.Activities.Add(new Activity
        {
            ActivityType = FollowUpRules.ActivityTypeFor(f.FollowUpType),
            Subject = Truncate($"Follow-up completed: {f.Subject}", 200),
            Description = f.Outcome,
            ActivityDate = Clock.Now,
            Status = ActivityStatus.Completed,
            CustomerId = f.CustomerId,
            LeadId = f.LeadId,
            OpportunityId = f.OpportunityId,
            FollowUpId = f.FollowUpId,
            AssignedToId = f.AssignedToId
        });

        // Update the related CRM record: contacting a New lead moves it to Contacted.
        if (f.LeadId is { } leadId)
        {
            var lead = await Db.Leads.FirstOrDefaultAsync(l => l.LeadId == leadId, ct);
            if (lead is { Status: LeadStatus.New })
            {
                lead.Status = LeadStatus.Contacted;
                Db.SetAuditHint(lead, AuditAction.StatusChange, $"Lead moved to Contacted after follow-up #{f.FollowUpId} was completed.");
            }
        }

        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult<FollowUpDto>> RescheduleAsync(int id, FollowUpRescheduleRequest request, CancellationToken ct = default)
    {
        var (f, failure) = await LoadAsync(id, ct);
        if (failure is not null) return failure;
        if (!FollowUpRules.CanReschedule(f!.Status))
            return ServiceResult<FollowUpDto>.Invalid("", $"A {f.Status} follow-up cannot be rescheduled.");
        var errors = Validator.Validate(request);
        if (request.NewDate is { } nd) errors.Merge(FollowUpRules.ValidateDate(nd, Clock.Today, nameof(FollowUpRescheduleRequest.NewDate)).Errors);
        if (errors.Count > 0) return ServiceResult<FollowUpDto>.Invalid(errors);

        var old = f.FollowUpDate;
        f.FollowUpDate = request.NewDate!.Value;
        f.Status = FollowUpStatus.Planned;
        if (!string.IsNullOrWhiteSpace(request.Reason))
            f.Remarks = Truncate($"{f.Remarks}\nRescheduled from {old:dd-MMM-yyyy HH:mm}: {request.Reason.Trim()}".Trim(), 1000);
        Db.SetAuditHint(f, AuditAction.Reschedule, $"Rescheduled from {old:yyyy-MM-dd HH:mm} to {f.FollowUpDate:yyyy-MM-dd HH:mm}.");
        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult<FollowUpDto>> MarkMissedAsync(int id, CancellationToken ct = default)
    {
        var (f, failure) = await LoadAsync(id, ct);
        if (failure is not null) return failure;
        if (!FollowUpRules.CanMarkMissed(f!.Status))
            return ServiceResult<FollowUpDto>.Invalid("", $"A {f.Status} follow-up cannot be marked as missed.");
        f.Status = FollowUpStatus.Missed;
        Db.SetAuditHint(f, AuditAction.StatusChange, "Follow-up marked as Missed.");
        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult<FollowUpDto>> CancelAsync(int id, CancellationToken ct = default)
    {
        var (f, failure) = await LoadAsync(id, ct);
        if (failure is not null) return failure;
        if (!FollowUpRules.CanCancel(f!.Status))
            return ServiceResult<FollowUpDto>.Invalid("", $"A {f.Status} follow-up cannot be cancelled.");
        f.Status = FollowUpStatus.Cancelled;
        Db.SetAuditHint(f, AuditAction.StatusChange, "Follow-up cancelled.");
        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        if (!Scope.CanDelete) return ServiceResult.Forbidden("Only Managers and Admins can delete follow-ups.");
        var (f, failure) = await LoadAsync(id, ct);
        if (failure is not null) return failure;
        Db.FollowUps.Remove(f!);
        await Db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<(int Overdue, int DueToday, IReadOnlyList<FollowUpDto> Items)> RemindersForCurrentUserAsync(CancellationToken ct = default)
    {
        var me = Scope.UserId;
        var now = Clock.Now;
        var endOfToday = Clock.Today.AddDays(1);
        var q = Db.FollowUps.AsNoTracking().Where(f => f.AssignedToId == me && f.Status == FollowUpStatus.Planned && f.FollowUpDate < endOfToday);
        var overdue = await q.CountAsync(f => f.FollowUpDate < now, ct);
        var dueToday = await q.CountAsync(f => f.FollowUpDate >= now, ct);
        var rows = await q.OrderBy(f => f.FollowUpDate).Take(6).Select(Row).ToListAsync(ct);
        return (overdue, dueToday, rows.Select(r => ToDto(r, now)).ToList());
    }

    private async Task<(FollowUp?, ServiceResult<FollowUpDto>?)> LoadAsync(int id, CancellationToken ct)
    {
        var f = await Db.FollowUps.FirstOrDefaultAsync(x => x.FollowUpId == id, ct);
        if (f is null) return (null, ServiceResult<FollowUpDto>.NotFound());
        if (!await Scope.CanAccessAsync(f, ct)) return (null, ServiceResult<FollowUpDto>.Forbidden());
        return (f, null);
    }

    private async Task<RelatedRecordDto?> ResolveRelatedAsync(FollowUpEditRequest r, Dictionary<string, List<string>> errors, bool required, CancellationToken ct)
    {
        if (r.RelatedType is null || r.RelatedId is null)
        {
            if (required) errors.Add(nameof(FollowUpEditRequest.RelatedId), "Select the customer, lead or opportunity this follow-up is for.");
            return null;
        }
        var related = await lookups.ResolveRelatedAsync(r.RelatedType.Value, r.RelatedId.Value, ct);
        if (related is null) errors.Add(nameof(FollowUpEditRequest.RelatedId), "Select a valid related record.");
        return related;
    }

    private static void SetRelated(FollowUp f, RelatedRecordDto? related)
    {
        if (related is null) return;
        f.CustomerId = related.Type == RelatedRecordType.Customer ? related.Id : null;
        f.LeadId = related.Type == RelatedRecordType.Lead ? related.Id : null;
        f.OpportunityId = related.Type == RelatedRecordType.Opportunity ? related.Id : null;
    }

    private sealed record FollowUpRow(int FollowUpId, string Subject, FollowUpType FollowUpType, DateTime FollowUpDate, FollowUpStatus Status,
        string? Remarks, string? Outcome, DateTime? CompletedDate, int? CustomerId, string? CustomerName, int? LeadId, string? LeadName,
        int? OpportunityId, string? OpportunityName, string AssignedToId, string? AssignedToName, DateTime CreatedDate);

    private static readonly System.Linq.Expressions.Expression<Func<FollowUp, FollowUpRow>> Row = f => new FollowUpRow(
        f.FollowUpId, f.Subject, f.FollowUpType, f.FollowUpDate, f.Status, f.Remarks, f.Outcome, f.CompletedDate,
        f.CustomerId, f.Customer != null ? f.Customer.CustomerName : null,
        f.LeadId, f.Lead != null ? f.Lead.LeadName : null,
        f.OpportunityId, f.Opportunity != null ? f.Opportunity.OpportunityName : null,
        f.AssignedToId, f.AssignedTo != null ? f.AssignedTo.FullName : null, f.CreatedDate);

    private static FollowUpDto ToDto(FollowUpRow r, DateTime now) => new()
    {
        FollowUpId = r.FollowUpId,
        Subject = r.Subject,
        FollowUpType = r.FollowUpType,
        FollowUpDate = r.FollowUpDate,
        Status = r.Status,
        IsOverdue = FollowUpRules.IsOverdue(r.Status, r.FollowUpDate, now),
        Remarks = r.Remarks,
        Outcome = r.Outcome,
        CompletedDate = r.CompletedDate,
        CustomerId = r.CustomerId,
        LeadId = r.LeadId,
        OpportunityId = r.OpportunityId,
        RelatedType = r.CustomerId.HasValue ? RelatedRecordType.Customer : r.LeadId.HasValue ? RelatedRecordType.Lead
            : r.OpportunityId.HasValue ? RelatedRecordType.Opportunity : null,
        RelatedId = r.CustomerId ?? r.LeadId ?? r.OpportunityId,
        RelatedName = r.CustomerName ?? r.LeadName ?? r.OpportunityName,
        AssignedToId = r.AssignedToId,
        AssignedToName = r.AssignedToName,
        CreatedDate = r.CreatedDate
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
