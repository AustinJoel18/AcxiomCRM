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

public interface ILeadService
{
    Task<PagedResult<LeadDto>> SearchAsync(LeadQuery query, CancellationToken ct = default);
    Task<ServiceResult<LeadDto>> GetAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<LeadDto>> CreateAsync(LeadSaveRequest request, CancellationToken ct = default);
    Task<ServiceResult<LeadDto>> UpdateAsync(int id, LeadSaveRequest request, CancellationToken ct = default);
    Task<ServiceResult<LeadDto>> ChangeStatusAsync(int id, LeadStatusChangeRequest request, CancellationToken ct = default);
    Task<ServiceResult<LeadConvertResult>> ConvertAsync(int id, LeadConvertRequest request, CancellationToken ct = default);
    Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class LeadService(ApplicationDbContext db, IDataScope scope, IRequestValidator validator, IBusinessClock clock, ICustomerService customers)
    : CrmServiceBase(db, scope, validator, clock), ILeadService
{
    internal static readonly Expression<Func<Lead, LeadDto>> Projection = l => new LeadDto
    {
        LeadId = l.LeadId,
        LeadCode = l.LeadCode,
        LeadName = l.LeadName,
        Email = l.Email,
        Phone = l.Phone,
        CompanyName = l.CompanyName,
        Source = l.Source,
        Status = l.Status,
        Priority = l.Priority,
        ExpectedValue = l.ExpectedValue,
        Notes = l.Notes,
        AssignedToId = l.AssignedToId,
        AssignedToName = l.AssignedTo != null ? l.AssignedTo.FullName : null,
        CreatedDate = l.CreatedDate,
        ModifiedDate = l.ModifiedDate,
        ConvertedDate = l.ConvertedDate,
        ConvertedCustomerId = l.ConvertedCustomerId,
        ConvertedCustomerName = l.ConvertedCustomer != null ? l.ConvertedCustomer.CustomerName : null,
        ConvertedOpportunityId = l.ConvertedOpportunityId
    };

    public async Task<PagedResult<LeadDto>> SearchAsync(LeadQuery query, CancellationToken ct = default)
    {
        query.Normalize();
        var q = await Scope.ApplyAsync(Db.Leads.AsNoTracking(), ct);

        if (query.Search is { } s)
        {
            var like = $"%{s}%";
            q = q.Where(l => EF.Functions.Like(l.LeadName, like) || EF.Functions.Like(l.LeadCode, like)
                             || (l.CompanyName != null && EF.Functions.Like(l.CompanyName, like))
                             || (l.Email != null && EF.Functions.Like(l.Email, like))
                             || (l.Phone != null && EF.Functions.Like(l.Phone, like)));
        }
        if (query.Status is { } status) q = q.Where(l => l.Status == status);
        if (query.Source is { } source) q = q.Where(l => l.Source == source);
        if (!string.IsNullOrEmpty(query.AssignedToId)) q = q.Where(l => l.AssignedToId == query.AssignedToId);

        var d = query.Descending;
        q = query.Sort switch
        {
            "code" => d ? q.OrderByDescending(l => l.LeadCode) : q.OrderBy(l => l.LeadCode),
            "name" => d ? q.OrderByDescending(l => l.LeadName) : q.OrderBy(l => l.LeadName),
            "company" => d ? q.OrderByDescending(l => l.CompanyName) : q.OrderBy(l => l.CompanyName),
            "status" => d ? q.OrderByDescending(l => l.Status) : q.OrderBy(l => l.Status),
            "source" => d ? q.OrderByDescending(l => l.Source) : q.OrderBy(l => l.Source),
            "priority" => d ? q.OrderByDescending(l => l.Priority) : q.OrderBy(l => l.Priority),
            "value" => d ? q.OrderByDescending(l => l.ExpectedValue) : q.OrderBy(l => l.ExpectedValue),
            "owner" => d ? q.OrderByDescending(l => l.AssignedTo!.FullName) : q.OrderBy(l => l.AssignedTo!.FullName),
            "created" when !d => q.OrderBy(l => l.LeadId),
            _ => q.OrderByDescending(l => l.LeadId)
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(Projection).ToListAsync(ct);
        return new PagedResult<LeadDto> { Items = items.Select(Enrich).ToList(), Page = query.Page, PageSize = query.PageSize, TotalCount = total };
    }

    public async Task<ServiceResult<LeadDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var owner = await Db.Leads.AsNoTracking().Where(l => l.LeadId == id).Select(l => l.AssignedToId).FirstOrDefaultAsync(ct);
        if (owner is null) return ServiceResult<LeadDto>.NotFound();
        var ids = await Scope.VisibleOwnerIdsAsync(ct);
        if (ids is not null && !ids.Contains(owner)) return ServiceResult<LeadDto>.Forbidden();
        var dto = await Db.Leads.AsNoTracking().Where(l => l.LeadId == id).Select(Projection).FirstAsync(ct);
        return ServiceResult<LeadDto>.Ok(Enrich(dto));
    }

    public async Task<ServiceResult<LeadDto>> CreateAsync(LeadSaveRequest request, CancellationToken ct = default)
    {
        var errors = Validator.Validate(request);
        if (request.Status is { } st && !LeadWorkflow.InitialStatuses.Contains(st))
            errors.Add(nameof(LeadSaveRequest.Status), "A new lead must start as New, Contacted or Qualified.");
        var (assignee, assignError) = await ResolveAssigneeAsync(request.AssignedToId, null, ct);
        if (assignError is not null) errors.Add(nameof(LeadSaveRequest.AssignedToId), assignError);
        if (errors.Count > 0) return ServiceResult<LeadDto>.Invalid(errors);

        var lead = new Lead { AssignedToId = assignee!, Status = request.Status!.Value };
        Apply(lead, request);
        var maxId = await Db.Leads.IgnoreQueryFilters().MaxAsync(l => (int?)l.LeadId, ct);
        lead.LeadCode = NextCode("LEA", maxId);

        Db.Leads.Add(lead);
        try { await Db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return ServiceResult<LeadDto>.Conflict("", "The lead could not be saved because of a duplicate code. Please retry."); }
        return await GetAsync(lead.LeadId, ct);
    }

    public async Task<ServiceResult<LeadDto>> UpdateAsync(int id, LeadSaveRequest request, CancellationToken ct = default)
    {
        var lead = await Db.Leads.FirstOrDefaultAsync(l => l.LeadId == id, ct);
        if (lead is null) return ServiceResult<LeadDto>.NotFound();
        if (!await Scope.CanAccessAsync(lead, ct)) return ServiceResult<LeadDto>.Forbidden();
        if (lead.Status == LeadStatus.Converted)
            return ServiceResult<LeadDto>.Invalid("", "Converted leads are read-only.");

        var errors = Validator.Validate(request);
        var transition = ValidateTransition(lead.Status, request.Status);
        if (transition is not null) errors.Add(nameof(LeadSaveRequest.Status), transition);
        var (assignee, assignError) = await ResolveAssigneeAsync(request.AssignedToId, lead.AssignedToId, ct);
        if (assignError is not null) errors.Add(nameof(LeadSaveRequest.AssignedToId), assignError);
        if (errors.Count > 0) return ServiceResult<LeadDto>.Invalid(errors);

        var oldStatus = lead.Status;
        var oldOwner = lead.AssignedToId;
        Apply(lead, request);
        lead.Status = request.Status!.Value;
        lead.AssignedToId = assignee!;

        if (oldStatus != lead.Status)
            Db.SetAuditHint(lead, AuditAction.StatusChange, $"Lead status changed from {oldStatus} to {lead.Status}.");
        else if (oldOwner != lead.AssignedToId)
            Db.SetAuditHint(lead, AuditAction.Assign, "Lead re-assigned.");

        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult<LeadDto>> ChangeStatusAsync(int id, LeadStatusChangeRequest request, CancellationToken ct = default)
    {
        var lead = await Db.Leads.FirstOrDefaultAsync(l => l.LeadId == id, ct);
        if (lead is null) return ServiceResult<LeadDto>.NotFound();
        if (!await Scope.CanAccessAsync(lead, ct)) return ServiceResult<LeadDto>.Forbidden();

        var errors = Validator.Validate(request);
        var transition = ValidateTransition(lead.Status, request.Status);
        if (transition is not null) errors.Add(nameof(LeadStatusChangeRequest.Status), transition);
        if (errors.Count > 0) return ServiceResult<LeadDto>.Invalid(errors);
        if (lead.Status == request.Status) return await GetAsync(id, ct);

        var old = lead.Status;
        lead.Status = request.Status!.Value;
        if (!string.IsNullOrWhiteSpace(request.Remarks))
            lead.Notes = Truncate($"{lead.Notes}\n[{Clock.Now:dd-MMM-yyyy HH:mm}] {old} → {lead.Status}: {request.Remarks.Trim()}".Trim(), 2000);
        Db.SetAuditHint(lead, AuditAction.StatusChange, $"Lead status changed from {old} to {lead.Status}." +
                                                       (string.IsNullOrWhiteSpace(request.Remarks) ? "" : $" Remarks: {request.Remarks.Trim()}"));
        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult<LeadConvertResult>> ConvertAsync(int id, LeadConvertRequest request, CancellationToken ct = default)
    {
        var lead = await Db.Leads.FirstOrDefaultAsync(l => l.LeadId == id, ct);
        if (lead is null) return ServiceResult<LeadConvertResult>.NotFound();
        if (!await Scope.CanAccessAsync(lead, ct)) return ServiceResult<LeadConvertResult>.Forbidden();
        if (!LeadWorkflow.CanConvert(lead.Status))
            return ServiceResult<LeadConvertResult>.Invalid("", $"Only Qualified leads can be converted. This lead is {lead.Status}.");

        var errors = new Dictionary<string, List<string>>();
        Customer? newCustomer = null;
        Customer? existing = null;

        if (request.ExistingCustomerId is { } existingId)
        {
            existing = await Db.Customers.FirstOrDefaultAsync(c => c.CustomerId == existingId, ct);
            if (existing is null || !await Scope.CanAccessAsync(existing, ct))
                errors.Add(nameof(LeadConvertRequest.ExistingCustomerId), "Select a valid customer.");
        }
        else if (request.Customer is null)
        {
            errors.Add(nameof(LeadConvertRequest.Customer), "Customer details are required to convert the lead.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Customer.AssignedToId)) request.Customer.AssignedToId = lead.AssignedToId;
            var built = await customers.BuildNewAsync(request.Customer, ct);
            if (built.Succeeded) newCustomer = built.Value;
            else if (built.Status == ResultStatus.Conflict)
                return ServiceResult<LeadConvertResult>.Conflict("Customer." + built.Errors.Keys.First(), built.Message!);
            else errors.Merge(Prefix(built.Errors, "Customer."));
        }

        if (request.Opportunity is { } opp)
        {
            errors.Merge(Validator.Validate(opp, "Opportunity."));
            if (opp is { Amount: { } amount, Probability: { } prob, ExpectedCloseDate: { } close })
                errors.Merge(Prefix(OpportunityRules.Validate(OpportunityStage.Qualification, amount, prob, close, Clock.Today, null, null).Errors, "Opportunity."));
        }
        if (errors.Count > 0) return ServiceResult<LeadConvertResult>.Invalid(errors);

        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        var customer = existing ?? newCustomer!;
        if (newCustomer is not null)
        {
            newCustomer.SourceLeadId = lead.LeadId;
            Db.Customers.Add(newCustomer);
        }

        Opportunity? opportunity = null;
        if (request.Opportunity is { } o)
        {
            opportunity = new Opportunity
            {
                OpportunityName = o.OpportunityName!.Trim(),
                Customer = customer,
                LeadId = lead.LeadId,
                Amount = o.Amount!.Value,
                Probability = o.Probability!.Value,
                ExpectedCloseDate = o.ExpectedCloseDate!.Value.Date,
                Stage = OpportunityStage.Qualification,
                Status = OpportunityStatus.Open,
                Source = lead.Source,
                AssignedToId = customer.AssignedToId,
                Notes = $"Created from lead {lead.LeadCode}."
            };
            Db.Opportunities.Add(opportunity);
        }

        try { await Db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return ServiceResult<LeadConvertResult>.Conflict("", "A customer with the same email or phone already exists. Link the existing customer instead.");
        }

        lead.Status = LeadStatus.Converted;
        lead.ConvertedDate = Clock.UtcNow;
        lead.ConvertedCustomerId = customer.CustomerId;
        lead.ConvertedOpportunityId = opportunity?.OpportunityId;
        Db.SetAuditHint(lead, AuditAction.Convert,
            $"Lead {lead.LeadCode} converted to customer {customer.CustomerCode}" +
            (opportunity is null ? "." : $" with opportunity #{opportunity.OpportunityId} ({opportunity.OpportunityName})."));
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult<LeadConvertResult>.Ok(new LeadConvertResult(lead.LeadId, customer.CustomerId, customer.CustomerCode, opportunity?.OpportunityId));
    }

    public async Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        if (!Scope.CanDelete) return ServiceResult.Forbidden("Only Managers and Admins can delete leads.");
        var lead = await Db.Leads.FirstOrDefaultAsync(l => l.LeadId == id, ct);
        if (lead is null) return ServiceResult.NotFound();
        if (!await Scope.CanAccessAsync(lead, ct)) return ServiceResult.Forbidden();
        if (lead.Status == LeadStatus.Converted)
            return ServiceResult.Conflict("", "Converted leads are kept for conversion history and cannot be deleted.");

        Db.Leads.Remove(lead);
        await Db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    private static string? ValidateTransition(LeadStatus current, LeadStatus? requested)
    {
        if (requested is not { } to || to == current) return null;
        if (to == LeadStatus.Converted) return "Use Convert Lead to convert a qualified lead.";
        if (LeadWorkflow.CanTransition(current, to)) return null;
        var allowed = LeadWorkflow.NextStatuses(current);
        return allowed.Count == 0
            ? $"A {current} lead cannot change status."
            : $"Invalid status change from {current} to {to}. Allowed: {string.Join(", ", allowed)}.";
    }

    private static void Apply(Lead l, LeadSaveRequest r)
    {
        l.LeadName = r.LeadName!.Trim();
        l.Email = Clean(r.Email)?.ToLowerInvariant();
        l.Phone = Clean(r.Phone);
        l.CompanyName = Clean(r.CompanyName);
        l.Source = r.Source!.Value;
        l.Priority = r.Priority!.Value;
        l.ExpectedValue = r.ExpectedValue;
        l.Notes = Clean(r.Notes);
    }

    private static LeadDto Enrich(LeadDto d) => new()
    {
        LeadId = d.LeadId, LeadCode = d.LeadCode, LeadName = d.LeadName, Email = d.Email, Phone = d.Phone,
        CompanyName = d.CompanyName, Source = d.Source, Status = d.Status, Priority = d.Priority,
        ExpectedValue = d.ExpectedValue, Notes = d.Notes, AssignedToId = d.AssignedToId, AssignedToName = d.AssignedToName,
        CreatedDate = d.CreatedDate, ModifiedDate = d.ModifiedDate, ConvertedDate = d.ConvertedDate,
        ConvertedCustomerId = d.ConvertedCustomerId, ConvertedCustomerName = d.ConvertedCustomerName,
        ConvertedOpportunityId = d.ConvertedOpportunityId,
        AllowedNextStatuses = LeadWorkflow.NextStatuses(d.Status),
        CanConvert = LeadWorkflow.CanConvert(d.Status)
    };

    private static Dictionary<string, List<string>> Prefix(Dictionary<string, List<string>> errors, string prefix) =>
        errors.ToDictionary(kv => string.IsNullOrEmpty(kv.Key) ? prefix.TrimEnd('.') : prefix + kv.Key, kv => kv.Value);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[^max..];
}
