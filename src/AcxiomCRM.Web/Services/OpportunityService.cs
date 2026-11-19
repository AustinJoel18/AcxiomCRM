using System.Linq.Expressions;
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

public interface IOpportunityService
{
    Task<PagedResult<OpportunityDto>> SearchAsync(OpportunityQuery query, CancellationToken ct = default);
    Task<ServiceResult<OpportunityDto>> GetAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<OpportunityDto>> CreateAsync(OpportunitySaveRequest request, CancellationToken ct = default);
    Task<ServiceResult<OpportunityDto>> UpdateAsync(int id, OpportunitySaveRequest request, CancellationToken ct = default);
    Task<ServiceResult<OpportunityDto>> ChangeStageAsync(int id, OpportunityStageChangeRequest request, CancellationToken ct = default);
    Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<OpportunityDto>> BoardAsync(string? ownerId, CancellationToken ct = default);
    Task<PipelineReportDto> PipelineAsync(string? ownerId = null, DateTime? createdFrom = null, DateTime? createdTo = null, CancellationToken ct = default);
}

public sealed class OpportunityService(ApplicationDbContext db, IDataScope scope, IRequestValidator validator, IBusinessClock clock)
    : CrmServiceBase(db, scope, validator, clock), IOpportunityService
{
    internal static readonly Expression<Func<Opportunity, OpportunityDto>> Projection = o => new OpportunityDto
    {
        OpportunityId = o.OpportunityId,
        OpportunityName = o.OpportunityName,
        CustomerId = o.CustomerId,
        CustomerName = o.Customer != null ? o.Customer.CustomerName : null,
        LeadId = o.LeadId,
        LeadName = o.Lead != null ? o.Lead.LeadName : null,
        Amount = o.Amount,
        Stage = o.Stage,
        Status = o.Status,
        Probability = o.Probability,
        WeightedAmount = OpportunityRules.Weighted(o.Amount, o.Probability), // client-evaluated in final projection
        ExpectedCloseDate = o.ExpectedCloseDate,
        Source = o.Source,
        Notes = o.Notes,
        CloseReason = o.CloseReason,
        ClosedDate = o.ClosedDate,
        AssignedToId = o.AssignedToId,
        AssignedToName = o.AssignedTo != null ? o.AssignedTo.FullName : null,
        CreatedDate = o.CreatedDate,
        ModifiedDate = o.ModifiedDate
    };

    public async Task<PagedResult<OpportunityDto>> SearchAsync(OpportunityQuery query, CancellationToken ct = default)
    {
        query.Normalize();
        var q = await Scope.ApplyAsync(Db.Opportunities.AsNoTracking(), ct);

        if (query.Search is { } s)
        {
            var like = $"%{s}%";
            q = q.Where(o => EF.Functions.Like(o.OpportunityName, like) || EF.Functions.Like(o.Customer!.CustomerName, like)
                             || (o.Customer!.CompanyName != null && EF.Functions.Like(o.Customer.CompanyName, like)));
        }
        if (query.Stage is { } stage) q = q.Where(o => o.Stage == stage);
        if (query.Status is { } status) q = q.Where(o => o.Status == status);
        if (query.CustomerId is { } cid) q = q.Where(o => o.CustomerId == cid);
        if (!string.IsNullOrEmpty(query.AssignedToId)) q = q.Where(o => o.AssignedToId == query.AssignedToId);

        var d = query.Descending;
        q = query.Sort switch
        {
            "name" => d ? q.OrderByDescending(o => o.OpportunityName) : q.OrderBy(o => o.OpportunityName),
            "customer" => d ? q.OrderByDescending(o => o.Customer!.CustomerName) : q.OrderBy(o => o.Customer!.CustomerName),
            "stage" => d ? q.OrderByDescending(o => o.Stage) : q.OrderBy(o => o.Stage),
            "amount" => d ? q.OrderByDescending(o => o.Amount) : q.OrderBy(o => o.Amount),
            "probability" => d ? q.OrderByDescending(o => o.Probability) : q.OrderBy(o => o.Probability),
            "close" => d ? q.OrderByDescending(o => o.ExpectedCloseDate) : q.OrderBy(o => o.ExpectedCloseDate),
            "owner" => d ? q.OrderByDescending(o => o.AssignedTo!.FullName) : q.OrderBy(o => o.AssignedTo!.FullName),
            "created" when !d => q.OrderBy(o => o.OpportunityId),
            _ => q.OrderByDescending(o => o.OpportunityId)
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(Projection).ToListAsync(ct);
        return new PagedResult<OpportunityDto> { Items = items, Page = query.Page, PageSize = query.PageSize, TotalCount = total };
    }

    public async Task<ServiceResult<OpportunityDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var owner = await Db.Opportunities.AsNoTracking().Where(o => o.OpportunityId == id).Select(o => o.AssignedToId).FirstOrDefaultAsync(ct);
        if (owner is null) return ServiceResult<OpportunityDto>.NotFound();
        var ids = await Scope.VisibleOwnerIdsAsync(ct);
        if (ids is not null && !ids.Contains(owner)) return ServiceResult<OpportunityDto>.Forbidden();
        return ServiceResult<OpportunityDto>.Ok(await Db.Opportunities.AsNoTracking().Where(o => o.OpportunityId == id).Select(Projection).FirstAsync(ct));
    }

    public async Task<ServiceResult<OpportunityDto>> CreateAsync(OpportunitySaveRequest request, CancellationToken ct = default)
    {
        Normalize(request);
        var errors = Validator.Validate(request);
        if (errors.Count == 0)
            errors.Merge(OpportunityRules.Validate(request.Stage!.Value, request.Amount!.Value, request.Probability!.Value,
                request.ExpectedCloseDate!.Value, Clock.Today, null, request.CloseReason).Errors);
        await ValidateReferencesAsync(request, errors, ct);
        var (assignee, assignError) = await ResolveAssigneeAsync(request.AssignedToId, null, ct);
        if (assignError is not null) errors.Add(nameof(OpportunitySaveRequest.AssignedToId), assignError);
        if (errors.Count > 0) return ServiceResult<OpportunityDto>.Invalid(errors);

        var opp = new Opportunity { AssignedToId = assignee! };
        Apply(opp, request);
        Db.Opportunities.Add(opp);
        await Db.SaveChangesAsync(ct);
        return await GetAsync(opp.OpportunityId, ct);
    }

    public async Task<ServiceResult<OpportunityDto>> UpdateAsync(int id, OpportunitySaveRequest request, CancellationToken ct = default)
    {
        var opp = await Db.Opportunities.FirstOrDefaultAsync(o => o.OpportunityId == id, ct);
        if (opp is null) return ServiceResult<OpportunityDto>.NotFound();
        if (!await Scope.CanAccessAsync(opp, ct)) return ServiceResult<OpportunityDto>.Forbidden();

        Normalize(request);
        var errors = Validator.Validate(request);
        if (errors.Count == 0)
            errors.Merge(OpportunityRules.Validate(request.Stage!.Value, request.Amount!.Value, request.Probability!.Value,
                request.ExpectedCloseDate!.Value, Clock.Today, opp.Stage, request.CloseReason).Errors);
        await ValidateReferencesAsync(request, errors, ct);
        var (assignee, assignError) = await ResolveAssigneeAsync(request.AssignedToId, opp.AssignedToId, ct);
        if (assignError is not null) errors.Add(nameof(OpportunitySaveRequest.AssignedToId), assignError);
        if (errors.Count > 0) return ServiceResult<OpportunityDto>.Invalid(errors);

        var oldStage = opp.Stage;
        var oldOwner = opp.AssignedToId;
        Apply(opp, request);
        opp.AssignedToId = assignee!;
        HintStageChange(opp, oldStage, oldOwner);
        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult<OpportunityDto>> ChangeStageAsync(int id, OpportunityStageChangeRequest request, CancellationToken ct = default)
    {
        var opp = await Db.Opportunities.FirstOrDefaultAsync(o => o.OpportunityId == id, ct);
        if (opp is null) return ServiceResult<OpportunityDto>.NotFound();
        if (!await Scope.CanAccessAsync(opp, ct)) return ServiceResult<OpportunityDto>.Forbidden();

        var errors = Validator.Validate(request);
        if (errors.Count > 0) return ServiceResult<OpportunityDto>.Invalid(errors);

        var stage = request.Stage!.Value;
        if (stage == opp.Stage) return await GetAsync(id, ct);
        var probability = OpportunityRules.DefaultProbability(stage);
        var rules = OpportunityRules.Validate(stage, opp.Amount, probability, opp.ExpectedCloseDate, Clock.Today, opp.Stage, request.CloseReason ?? opp.CloseReason);
        if (rules.Any) return ServiceResult<OpportunityDto>.Invalid(rules);

        var oldStage = opp.Stage;
        opp.Stage = stage;
        opp.Probability = probability;
        opp.Status = OpportunityRules.StatusFor(stage);
        if (!string.IsNullOrWhiteSpace(request.CloseReason)) opp.CloseReason = request.CloseReason.Trim();
        opp.ClosedDate = OpportunityRules.IsActive(stage) ? null : Clock.UtcNow;
        HintStageChange(opp, oldStage, opp.AssignedToId);
        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        if (!Scope.CanDelete) return ServiceResult.Forbidden("Only Managers and Admins can delete opportunities.");
        var opp = await Db.Opportunities.FirstOrDefaultAsync(o => o.OpportunityId == id, ct);
        if (opp is null) return ServiceResult.NotFound();
        if (!await Scope.CanAccessAsync(opp, ct)) return ServiceResult.Forbidden();
        Db.Opportunities.Remove(opp);
        await Db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<IReadOnlyList<OpportunityDto>> BoardAsync(string? ownerId, CancellationToken ct = default)
    {
        var q = await Scope.ApplyAsync(Db.Opportunities.AsNoTracking(), ct);
        if (!string.IsNullOrEmpty(ownerId)) q = q.Where(o => o.AssignedToId == ownerId);
        var since = Clock.UtcNow.AddDays(-90);
        q = q.Where(o => o.Status == OpportunityStatus.Open || (o.ClosedDate != null && o.ClosedDate >= since));
        return await q.OrderBy(o => o.ExpectedCloseDate).Take(300).Select(Projection).ToListAsync(ct);
    }

    public async Task<PipelineReportDto> PipelineAsync(string? ownerId = null, DateTime? createdFrom = null, DateTime? createdTo = null, CancellationToken ct = default)
    {
        var q = await Scope.ApplyAsync(Db.Opportunities.AsNoTracking(), ct);
        if (!string.IsNullOrEmpty(ownerId)) q = q.Where(o => o.AssignedToId == ownerId);
        if (createdFrom is { } f) { var fu = Clock.ToUtc(f.Date); q = q.Where(o => o.CreatedDate >= fu); }
        if (createdTo is { } t) { var tu = Clock.ToUtc(t.Date.AddDays(1)); q = q.Where(o => o.CreatedDate < tu); }

        var rows = await q.Select(o => new { o.Stage, o.Amount, o.Probability, o.AssignedToId, Owner = o.AssignedTo!.FullName }).ToListAsync(ct);

        var stages = Enum.GetValues<OpportunityStage>().Select(s =>
        {
            var items = rows.Where(r => r.Stage == s).ToList();
            return new PipelineStageDto(s, items.Count, items.Sum(i => i.Amount), items.Sum(i => OpportunityRules.Weighted(i.Amount, i.Probability)));
        }).ToList();

        var owners = rows.GroupBy(r => new { r.AssignedToId, r.Owner }).Select(g =>
        {
            var open = g.Where(r => OpportunityRules.IsActive(r.Stage)).ToList();
            var won = g.Where(r => r.Stage == OpportunityStage.Won).ToList();
            return new PipelineOwnerDto(g.Key.AssignedToId, g.Key.Owner, open.Count, open.Sum(r => r.Amount),
                open.Sum(r => OpportunityRules.Weighted(r.Amount, r.Probability)), won.Count, won.Sum(r => r.Amount),
                g.Count(r => r.Stage == OpportunityStage.Lost));
        }).OrderByDescending(o => o.OpenAmount).ToList();

        var openRows = rows.Where(r => OpportunityRules.IsActive(r.Stage)).ToList();
        return new PipelineReportDto
        {
            Stages = stages,
            Owners = owners,
            OpenCount = openRows.Count,
            TotalOpenAmount = openRows.Sum(r => r.Amount),
            TotalWeightedAmount = openRows.Sum(r => OpportunityRules.Weighted(r.Amount, r.Probability)),
            GeneratedAt = Clock.UtcNow,
            Scope = Scope.Description
        };
    }

    /// <summary>Won ⇒ 100 %, Lost ⇒ 0 % (business automation before validation).</summary>
    private static void Normalize(OpportunitySaveRequest r)
    {
        if (r.Stage == OpportunityStage.Won) r.Probability = 100;
        else if (r.Stage == OpportunityStage.Lost) r.Probability = 0;
    }

    private async Task ValidateReferencesAsync(OpportunitySaveRequest r, Dictionary<string, List<string>> errors, CancellationToken ct)
    {
        if (r.CustomerId is { } cid)
        {
            var cq = await Scope.ApplyAsync(Db.Customers.AsNoTracking(), ct);
            if (!await cq.AnyAsync(c => c.CustomerId == cid, ct))
                errors.Add(nameof(OpportunitySaveRequest.CustomerId), "Select a valid customer.");
        }
        if (r.LeadId is { } lid)
        {
            var lq = await Scope.ApplyAsync(Db.Leads.AsNoTracking(), ct);
            if (!await lq.AnyAsync(l => l.LeadId == lid, ct))
                errors.Add(nameof(OpportunitySaveRequest.LeadId), "Select a valid lead.");
        }
    }

    private void Apply(Opportunity o, OpportunitySaveRequest r)
    {
        var wasActive = o.OpportunityId == 0 || OpportunityRules.IsActive(o.Stage);
        o.OpportunityName = r.OpportunityName!.Trim();
        o.CustomerId = r.CustomerId!.Value;
        o.LeadId = r.LeadId;
        o.Amount = r.Amount!.Value;
        o.Stage = r.Stage!.Value;
        o.Probability = r.Probability!.Value;
        o.ExpectedCloseDate = r.ExpectedCloseDate!.Value.Date;
        o.Status = OpportunityRules.StatusFor(o.Stage);
        o.Source = r.Source;
        o.Notes = Clean(r.Notes);
        o.CloseReason = Clean(r.CloseReason);
        if (OpportunityRules.IsActive(o.Stage)) o.ClosedDate = null;
        else if (wasActive || o.ClosedDate is null) o.ClosedDate = Clock.UtcNow;
    }

    private void HintStageChange(Opportunity opp, OpportunityStage oldStage, string oldOwner)
    {
        if (oldStage != opp.Stage)
        {
            var details = opp.Stage switch
            {
                OpportunityStage.Won => $"Opportunity marked Won ({opp.Amount:N2}).",
                OpportunityStage.Lost => $"Opportunity marked Lost. Reason: {opp.CloseReason}",
                _ => $"Stage moved from {oldStage} to {opp.Stage}."
            };
            Db.SetAuditHint(opp, AuditAction.StatusChange, details);
        }
        else if (oldOwner != opp.AssignedToId)
        {
            Db.SetAuditHint(opp, AuditAction.Assign, "Opportunity re-assigned.");
        }
    }
}
