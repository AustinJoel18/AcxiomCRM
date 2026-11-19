using AcxiomCRM.Web.Domain;
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
public sealed class LeadsController(
    ILeadService leads,
    IFollowUpService followUps,
    IActivityService activities,
    IAuditService audit,
    ILookupService lookups,
    IBusinessClock clock,
    IDataScope scope) : CrmController
{
    public async Task<IActionResult> Index([FromQuery] LeadQuery query, CancellationToken ct) =>
        View(new ListPageViewModel<LeadDto, LeadQuery>
        {
            Query = query,
            Results = await leads.SearchAsync(query, ct),
            Owners = (await lookups.VisibleUsersAsync(ct)).ToSelectList(query.AssignedToId),
            CanDelete = scope.CanDelete,
            Scope = scope.Description
        });

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await leads.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        return View(await DetailsModelAsync(result.Value!, ct));
    }

    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new LeadFormViewModel { AssignedToId = scope.UserId, Status = LeadStatus.New, Priority = LeadPriority.Medium };
        await PopulateAsync(vm, null, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LeadFormViewModel vm, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await leads.CreateAsync(vm, ct);
            if (result.Succeeded)
            {
                Success($"Lead {result.Value!.LeadCode} created.");
                return RedirectToAction(nameof(Details), new { id = result.Value.LeadId });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result);
        }
        await PopulateAsync(vm, null, ct);
        return View("Form", vm);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await leads.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        var l = result.Value!;
        if (l.Status == LeadStatus.Converted)
        {
            Failure("Converted leads are read-only.");
            return RedirectToAction(nameof(Details), new { id });
        }
        var vm = new LeadFormViewModel
        {
            LeadId = l.LeadId, LeadCode = l.LeadCode, LeadName = l.LeadName, Email = l.Email, Phone = l.Phone, CompanyName = l.CompanyName,
            Source = l.Source, Status = l.Status, Priority = l.Priority, ExpectedValue = l.ExpectedValue, Notes = l.Notes, AssignedToId = l.AssignedToId
        };
        await PopulateAsync(vm, l.Status, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LeadFormViewModel vm, CancellationToken ct)
    {
        vm.LeadId = id;
        var current = await leads.GetAsync(id, ct);
        if (Deny(current) is { } denied) return denied;
        if (ModelState.IsValid)
        {
            var result = await leads.UpdateAsync(id, vm, ct);
            if (result.Succeeded)
            {
                Success("Lead updated.");
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddServiceErrors(result);
        }
        vm.LeadCode = current.Value!.LeadCode;
        await PopulateAsync(vm, current.Value.Status, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, [Bind(Prefix = "StatusChange")] LeadStatusChangeRequest request, CancellationToken ct)
    {
        var result = await leads.ChangeStatusAsync(id, request, ct);
        if (Deny(result) is { } denied) return denied;
        if (result.Succeeded) Success($"Lead status changed to {result.Value!.Status}.");
        else Failure(result.Message ?? "The status could not be changed.");
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Convert(int id, CancellationToken ct)
    {
        var result = await leads.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        var l = result.Value!;
        if (!l.CanConvert)
        {
            Failure($"Only Qualified leads can be converted. This lead is {l.Status}.");
            return RedirectToAction(nameof(Details), new { id });
        }
        var vm = new LeadConvertViewModel
        {
            Lead = l,
            Customer = new CustomerSaveRequest
            {
                CustomerName = l.LeadName, Email = l.Email, Phone = l.Phone, CompanyName = l.CompanyName, Status = CustomerStatus.Active,
                AssignedToId = l.AssignedToId, Notes = $"Converted from lead {l.LeadCode}."
            },
            Opportunity = new LeadConvertOpportunity
            {
                OpportunityName = $"{l.CompanyName ?? l.LeadName} — New Business",
                Amount = l.ExpectedValue is > 0 ? l.ExpectedValue : null,
                Probability = 20,
                ExpectedCloseDate = clock.Today.AddDays(30)
            }
        };
        await PopulateConvertAsync(vm, ct);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(int id, LeadConvertViewModel vm, CancellationToken ct)
    {
        var lead = await leads.GetAsync(id, ct);
        if (Deny(lead) is { } denied) return denied;
        vm.Lead = lead.Value!;

        // Only validate the sections the user actually chose.
        foreach (var key in ModelState.Keys.ToList())
        {
            if (vm.UseExistingCustomer && key.StartsWith("Customer.")) ModelState.Remove(key);
            if (!vm.CreateOpportunity && key.StartsWith("Opportunity.")) ModelState.Remove(key);
        }
        if (vm.UseExistingCustomer && vm.ExistingCustomerId is null)
            ModelState.AddModelError(nameof(vm.ExistingCustomerId), "Select the customer to link this lead to.");

        if (ModelState.IsValid)
        {
            var result = await leads.ConvertAsync(id, new LeadConvertRequest
            {
                ExistingCustomerId = vm.UseExistingCustomer ? vm.ExistingCustomerId : null,
                Customer = vm.UseExistingCustomer ? null : vm.Customer,
                Opportunity = vm.CreateOpportunity ? vm.Opportunity : null
            }, ct);
            if (result.Succeeded)
            {
                Success($"Lead converted to customer {result.Value!.CustomerCode}" + (result.Value.OpportunityId is null ? "." : " and an opportunity was created."));
                return result.Value.OpportunityId is { } oppId
                    ? RedirectToAction("Details", "Opportunities", new { id = oppId })
                    : RedirectToAction("Details", "Customers", new { id = result.Value.CustomerId });
            }
            if (Deny(result) is { } d2) return d2;
            ModelState.AddServiceErrors(result);
        }
        await PopulateConvertAsync(vm, ct);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.CanDeleteRecords)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await leads.DeleteAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        if (!result.Succeeded)
        {
            Failure(result.Message ?? "The lead could not be deleted.");
            return RedirectToAction(nameof(Details), new { id });
        }
        Success("Lead deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<LeadDetailsViewModel> DetailsModelAsync(LeadDto lead, CancellationToken ct) => new()
    {
        Lead = lead,
        FollowUps = (await followUps.SearchAsync(new FollowUpQuery { RelatedType = RelatedRecordType.Lead, RelatedId = lead.LeadId, PageSize = 50 }, ct)).Items,
        Activities = (await activities.SearchAsync(new ActivityQuery { PageSize = 50 }, RelatedRecordType.Lead, lead.LeadId, ct)).Items,
        History = await audit.ForRecordAsync("Lead", lead.LeadId.ToString(), ct: ct),
        CanDelete = scope.CanDelete && lead.Status != LeadStatus.Converted
    };

    private async Task PopulateAsync(LeadFormViewModel vm, LeadStatus? current, CancellationToken ct)
    {
        vm.Owners = (await lookups.AssignableUsersAsync(vm.LeadId is null ? null : vm.AssignedToId, ct)).ToSelectList(vm.AssignedToId);
        var allowed = current is { } c ? new[] { c }.Concat(LeadWorkflow.NextStatuses(c)) : LeadWorkflow.InitialStatuses;
        vm.StatusOptions = Ui.EnumOptions(allowed.Distinct());
    }

    private async Task PopulateConvertAsync(LeadConvertViewModel vm, CancellationToken ct)
    {
        vm.Customers = (await lookups.CustomersAsync(ct)).ToSelectList(vm.ExistingCustomerId?.ToString());
        vm.Owners = (await lookups.AssignableUsersAsync(vm.Lead.AssignedToId, ct)).ToSelectList(vm.Customer.AssignedToId);
    }
}
