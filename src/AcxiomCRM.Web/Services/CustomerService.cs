using System.Linq.Expressions;
using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Validation;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Web.Services;

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> SearchAsync(CustomerQuery query, CancellationToken ct = default);
    Task<ServiceResult<CustomerDto>> GetAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<CustomerDto>> CreateAsync(CustomerSaveRequest request, CancellationToken ct = default);
    Task<ServiceResult<CustomerDto>> UpdateAsync(int id, CustomerSaveRequest request, CancellationToken ct = default);
    Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Validates and builds (but does not save) a new customer. Used by lead conversion.</summary>
    Task<ServiceResult<Customer>> BuildNewAsync(CustomerSaveRequest request, CancellationToken ct = default);
}

public sealed class CustomerService(ApplicationDbContext db, IDataScope scope, IRequestValidator validator, IBusinessClock clock)
    : CrmServiceBase(db, scope, validator, clock), ICustomerService
{
    internal static readonly Expression<Func<Customer, CustomerDto>> Projection = c => new CustomerDto
    {
        CustomerId = c.CustomerId,
        CustomerCode = c.CustomerCode,
        CustomerName = c.CustomerName,
        Email = c.Email,
        Phone = c.Phone,
        CompanyName = c.CompanyName,
        Address = c.Address,
        City = c.City,
        State = c.State,
        Status = c.Status,
        Notes = c.Notes,
        AssignedToId = c.AssignedToId,
        AssignedToName = c.AssignedTo != null ? c.AssignedTo.FullName : null,
        SourceLeadId = c.SourceLeadId,
        CreatedDate = c.CreatedDate,
        CreatedByName = c.CreatedBy != null ? c.CreatedBy.FullName : null,
        ModifiedDate = c.ModifiedDate
    };

    public async Task<PagedResult<CustomerDto>> SearchAsync(CustomerQuery query, CancellationToken ct = default)
    {
        query.Normalize();
        var q = await Scope.ApplyAsync(Db.Customers.AsNoTracking(), ct);

        if (query.Search is { } s)
        {
            var like = $"%{s}%";
            q = q.Where(c => EF.Functions.Like(c.CustomerName, like) || EF.Functions.Like(c.Email, like)
                             || EF.Functions.Like(c.Phone, like) || (c.CompanyName != null && EF.Functions.Like(c.CompanyName, like))
                             || EF.Functions.Like(c.CustomerCode, like));
        }
        if (query.Status is { } status) q = q.Where(c => c.Status == status);
        if (!string.IsNullOrEmpty(query.AssignedToId)) q = q.Where(c => c.AssignedToId == query.AssignedToId);

        var d = query.Descending;
        q = query.Sort switch
        {
            "code" => d ? q.OrderByDescending(c => c.CustomerCode) : q.OrderBy(c => c.CustomerCode),
            "name" => d ? q.OrderByDescending(c => c.CustomerName) : q.OrderBy(c => c.CustomerName),
            "company" => d ? q.OrderByDescending(c => c.CompanyName) : q.OrderBy(c => c.CompanyName),
            "email" => d ? q.OrderByDescending(c => c.Email) : q.OrderBy(c => c.Email),
            "status" => d ? q.OrderByDescending(c => c.Status) : q.OrderBy(c => c.Status),
            "owner" => d ? q.OrderByDescending(c => c.AssignedTo!.FullName) : q.OrderBy(c => c.AssignedTo!.FullName),
            "created" when !d => q.OrderBy(c => c.CustomerId),
            _ => q.OrderByDescending(c => c.CustomerId)
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(Projection).ToListAsync(ct);
        return new PagedResult<CustomerDto> { Items = items, Page = query.Page, PageSize = query.PageSize, TotalCount = total };
    }

    public async Task<ServiceResult<CustomerDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var owner = await Db.Customers.AsNoTracking().Where(c => c.CustomerId == id).Select(c => c.AssignedToId).FirstOrDefaultAsync(ct);
        if (owner is null) return ServiceResult<CustomerDto>.NotFound();
        if (!await InScopeAsync(owner, ct)) return ServiceResult<CustomerDto>.Forbidden();
        var dto = await Db.Customers.AsNoTracking().Where(c => c.CustomerId == id).Select(Projection).FirstAsync(ct);
        return ServiceResult<CustomerDto>.Ok(dto);
    }

    public async Task<ServiceResult<Customer>> BuildNewAsync(CustomerSaveRequest request, CancellationToken ct = default)
    {
        var errors = Validator.Validate(request);
        var (assignee, assignError) = await ResolveAssigneeAsync(request.AssignedToId, null, ct);
        if (assignError is not null) errors.Add(nameof(CustomerSaveRequest.AssignedToId), assignError);
        if (errors.Count > 0) return ServiceResult<Customer>.Invalid(errors);

        var customer = new Customer { AssignedToId = assignee! };
        Apply(customer, request);

        var duplicate = await DuplicateCheckAsync(customer, ct);
        if (duplicate is not null) return ServiceResult<Customer>.From(duplicate);

        var maxId = await Db.Customers.IgnoreQueryFilters().MaxAsync(c => (int?)c.CustomerId, ct);
        var pendingCount = Db.ChangeTracker.Entries<Customer>().Count(e => e.State == EntityState.Added);
        customer.CustomerCode = NextCode("CUS", (maxId ?? 0) + pendingCount);
        return ServiceResult<Customer>.Ok(customer);
    }

    public async Task<ServiceResult<CustomerDto>> CreateAsync(CustomerSaveRequest request, CancellationToken ct = default)
    {
        var built = await BuildNewAsync(request, ct);
        if (!built.Succeeded) return ServiceResult<CustomerDto>.From(built);

        Db.Customers.Add(built.Value!);
        var saved = await SaveAsync(ct);
        if (saved is not null) return ServiceResult<CustomerDto>.From(saved);
        return await GetAsync(built.Value!.CustomerId, ct);
    }

    public async Task<ServiceResult<CustomerDto>> UpdateAsync(int id, CustomerSaveRequest request, CancellationToken ct = default)
    {
        var customer = await Db.Customers.FirstOrDefaultAsync(c => c.CustomerId == id, ct);
        if (customer is null) return ServiceResult<CustomerDto>.NotFound();
        if (!await Scope.CanAccessAsync(customer, ct)) return ServiceResult<CustomerDto>.Forbidden();

        var errors = Validator.Validate(request);
        var (assignee, assignError) = await ResolveAssigneeAsync(request.AssignedToId, customer.AssignedToId, ct);
        if (assignError is not null) errors.Add(nameof(CustomerSaveRequest.AssignedToId), assignError);
        if (errors.Count > 0) return ServiceResult<CustomerDto>.Invalid(errors);

        var oldStatus = customer.Status;
        var oldOwner = customer.AssignedToId;
        Apply(customer, request);
        customer.AssignedToId = assignee!;

        var duplicate = await DuplicateCheckAsync(customer, ct);
        if (duplicate is not null) return ServiceResult<CustomerDto>.From(duplicate);

        if (oldStatus != customer.Status)
            Db.SetAuditHint(customer, AuditAction.StatusChange, $"Status changed from {oldStatus} to {customer.Status}.");
        else if (oldOwner != customer.AssignedToId)
            Db.SetAuditHint(customer, AuditAction.Assign, "Customer re-assigned.");

        var saved = await SaveAsync(ct);
        if (saved is not null) return ServiceResult<CustomerDto>.From(saved);
        return await GetAsync(id, ct);
    }

    public async Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        if (!Scope.CanDelete) return ServiceResult.Forbidden("Only Managers and Admins can delete customers.");
        var customer = await Db.Customers.FirstOrDefaultAsync(c => c.CustomerId == id, ct);
        if (customer is null) return ServiceResult.NotFound();
        if (!await Scope.CanAccessAsync(customer, ct)) return ServiceResult.Forbidden();

        if (await Db.Opportunities.AnyAsync(o => o.CustomerId == id && o.Status == OpportunityStatus.Open, ct))
            return ServiceResult.Conflict("", "This customer has open opportunities. Close or reassign them before deleting the customer.");

        Db.Customers.Remove(customer); // soft delete (see ApplicationDbContext)
        await Db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    private static void Apply(Customer c, CustomerSaveRequest r)
    {
        c.CustomerName = r.CustomerName!.Trim();
        c.Email = r.Email!.Trim().ToLowerInvariant();
        c.Phone = r.Phone!.Trim();
        c.CompanyName = Clean(r.CompanyName);
        c.Address = Clean(r.Address);
        c.City = Clean(r.City);
        c.State = Clean(r.State);
        c.Status = r.Status!.Value;
        c.Notes = Clean(r.Notes);
    }

    /// <summary>Email, phone and name+company identify a customer; duplicates are rejected (409).</summary>
    private async Task<ServiceResult?> DuplicateCheckAsync(Customer c, CancellationToken ct)
    {
        var others = Db.Customers.AsNoTracking().Where(x => x.CustomerId != c.CustomerId);
        if (await others.AnyAsync(x => x.Email == c.Email, ct))
            return ServiceResult.Conflict(nameof(CustomerSaveRequest.Email), "A customer with this email address already exists.");
        if (await others.AnyAsync(x => x.Phone == c.Phone, ct))
            return ServiceResult.Conflict(nameof(CustomerSaveRequest.Phone), "A customer with this phone number already exists.");

        var name = c.CustomerName.ToLower();
        var company = c.CompanyName?.ToLower();
        if (await others.AnyAsync(x => x.CustomerName.ToLower() == name
                                       && (x.CompanyName == null ? company == null : x.CompanyName.ToLower() == company), ct))
            return ServiceResult.Conflict(nameof(CustomerSaveRequest.CustomerName),
                company is null ? "A customer with this name already exists." : "A customer with this name already exists for this company.");
        return null;
    }

    private async Task<ServiceResult?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await Db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return ServiceResult.Conflict("", "A customer with the same email, phone or code already exists.");
        }
    }

    private async Task<bool> InScopeAsync(string ownerId, CancellationToken ct)
    {
        var ids = await Scope.VisibleOwnerIdsAsync(ct);
        return ids is null || ids.Contains(ownerId);
    }
}
