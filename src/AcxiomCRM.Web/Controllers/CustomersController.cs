using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Services;
using AcxiomCRM.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Web.Controllers;

[Authorize(Policy = Policies.CrmUser)]
public sealed class CustomersController(
    ICustomerService customers,
    ILeadService leads,
    IOpportunityService opportunities,
    IFollowUpService followUps,
    IActivityService activities,
    IAuditService audit,
    ILookupService lookups,
    IDataScope scope) : CrmController
{
    public async Task<IActionResult> Index([FromQuery] CustomerQuery query, CancellationToken ct)
    {
        var results = await customers.SearchAsync(query, ct);
        return View(new ListPageViewModel<CustomerDto, CustomerQuery>
        {
            Query = query,
            Results = results,
            Owners = (await lookups.VisibleUsersAsync(ct)).ToSelectList(query.AssignedToId),
            CanDelete = scope.CanDelete,
            Scope = scope.Description
        });
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await customers.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        var c = result.Value!;
        LeadDto? sourceLead = null;
        if (c.SourceLeadId is { } leadId && (await leads.GetAsync(leadId, ct)) is { Succeeded: true } lr) sourceLead = lr.Value;

        return View(new CustomerDetailsViewModel
        {
            Customer = c,
            SourceLead = sourceLead,
            Opportunities = (await opportunities.SearchAsync(new OpportunityQuery { CustomerId = id, PageSize = 50 }, ct)).Items,
            FollowUps = (await followUps.SearchAsync(new FollowUpQuery { RelatedType = RelatedRecordType.Customer, RelatedId = id, PageSize = 50 }, ct)).Items,
            Activities = (await activities.SearchAsync(new ActivityQuery { PageSize = 50 }, RelatedRecordType.Customer, id, ct)).Items,
            History = await audit.ForRecordAsync("Customer", id.ToString(), ct: ct),
            CanDelete = scope.CanDelete
        });
    }

    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new CustomerFormViewModel { AssignedToId = scope.UserId };
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerFormViewModel vm, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await customers.CreateAsync(vm, ct);
            if (result.Succeeded)
            {
                Success($"Customer {result.Value!.CustomerCode} created.");
                return RedirectToAction(nameof(Details), new { id = result.Value.CustomerId });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result);
        }
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await customers.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        var c = result.Value!;
        var vm = new CustomerFormViewModel
        {
            CustomerId = c.CustomerId, CustomerCode = c.CustomerCode, CustomerName = c.CustomerName, Email = c.Email, Phone = c.Phone,
            CompanyName = c.CompanyName, Address = c.Address, City = c.City, State = c.State, Status = c.Status, Notes = c.Notes,
            AssignedToId = c.AssignedToId
        };
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CustomerFormViewModel vm, CancellationToken ct)
    {
        vm.CustomerId = id;
        if (ModelState.IsValid)
        {
            var result = await customers.UpdateAsync(id, vm, ct);
            if (result.Succeeded)
            {
                Success("Customer updated.");
                return RedirectToAction(nameof(Details), new { id });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result);
        }
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.CanDeleteRecords)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await customers.DeleteAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        if (!result.Succeeded)
        {
            Failure(result.Message ?? "The customer could not be deleted.");
            return RedirectToAction(nameof(Details), new { id });
        }
        Success("Customer deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateAsync(CustomerFormViewModel vm, CancellationToken ct)
    {
        if (vm.CustomerId is { } id && vm.CustomerCode is null)
            vm.CustomerCode = (await customers.GetAsync(id, ct)).Value?.CustomerCode;
        vm.Owners = (await lookups.AssignableUsersAsync(vm.CustomerId is null ? null : vm.AssignedToId, ct)).ToSelectList(vm.AssignedToId);
    }
}
