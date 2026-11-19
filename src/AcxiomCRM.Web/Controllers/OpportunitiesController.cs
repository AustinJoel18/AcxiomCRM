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
public sealed class OpportunitiesController(
    IOpportunityService opportunities,
    IFollowUpService followUps,
    IActivityService activities,
    IAuditService audit,
    ILookupService lookups,
    IBusinessClock clock,
    IDataScope scope) : CrmController
{
    public async Task<IActionResult> Index([FromQuery] OpportunityQuery query, CancellationToken ct) =>
        View(new ListPageViewModel<OpportunityDto, OpportunityQuery>
        {
            Query = query,
            Results = await opportunities.SearchAsync(query, ct),
            Owners = (await lookups.VisibleUsersAsync(ct)).ToSelectList(query.AssignedToId),
            CanDelete = scope.CanDelete,
            Scope = scope.Description
        });

    /// <summary>Kanban-style sales pipeline grouped by stage.</summary>
    public async Task<IActionResult> Pipeline(string? ownerId, CancellationToken ct)
    {
        ViewData["Owners"] = (await lookups.VisibleUsersAsync(ct)).ToSelectList(ownerId);
        ViewData["OwnerId"] = ownerId;
        return View(await opportunities.BoardAsync(ownerId, ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await opportunities.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        var o = result.Value!;
        return View(new OpportunityDetailsViewModel
        {
            Opportunity = o,
            FollowUps = (await followUps.SearchAsync(new FollowUpQuery { RelatedType = RelatedRecordType.Opportunity, RelatedId = id, PageSize = 50 }, ct)).Items,
            Activities = (await activities.SearchAsync(new ActivityQuery { PageSize = 50 }, RelatedRecordType.Opportunity, id, ct)).Items,
            History = await audit.ForRecordAsync("Opportunity", id.ToString(), ct: ct),
            StageChange = new OpportunityStageChangeRequest { Stage = o.Stage },
            CanDelete = scope.CanDelete
        });
    }

    public async Task<IActionResult> Create(int? customerId, CancellationToken ct)
    {
        var vm = new OpportunityFormViewModel
        {
            CustomerId = customerId, AssignedToId = scope.UserId, Stage = OpportunityStage.Qualification, Probability = 20,
            ExpectedCloseDate = clock.Today.AddDays(30)
        };
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OpportunityFormViewModel vm, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await opportunities.CreateAsync(vm, ct);
            if (result.Succeeded)
            {
                Success("Opportunity created.");
                return RedirectToAction(nameof(Details), new { id = result.Value!.OpportunityId });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result);
        }
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await opportunities.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        var o = result.Value!;
        var vm = new OpportunityFormViewModel
        {
            OpportunityId = o.OpportunityId, OpportunityName = o.OpportunityName, CustomerId = o.CustomerId, LeadId = o.LeadId, Amount = o.Amount,
            Stage = o.Stage, Probability = o.Probability, ExpectedCloseDate = o.ExpectedCloseDate, Source = o.Source, Notes = o.Notes,
            CloseReason = o.CloseReason, AssignedToId = o.AssignedToId
        };
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, OpportunityFormViewModel vm, CancellationToken ct)
    {
        vm.OpportunityId = id;
        if (ModelState.IsValid)
        {
            var result = await opportunities.UpdateAsync(id, vm, ct);
            if (result.Succeeded)
            {
                Success("Opportunity updated.");
                return RedirectToAction(nameof(Details), new { id });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result);
        }
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStage(int id, [Bind(Prefix = "StageChange")] OpportunityStageChangeRequest request, string? returnTo, CancellationToken ct)
    {
        var result = await opportunities.ChangeStageAsync(id, request, ct);
        if (Deny(result) is { } denied) return denied;
        if (result.Succeeded) Success($"Opportunity moved to {result.Value!.Stage}.");
        else Failure(result.Message ?? "The stage could not be changed.");
        return returnTo == "pipeline" ? RedirectToAction(nameof(Pipeline)) : RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.CanDeleteRecords)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await opportunities.DeleteAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        Success("Opportunity deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateAsync(OpportunityFormViewModel vm, CancellationToken ct)
    {
        vm.Owners = (await lookups.AssignableUsersAsync(vm.OpportunityId is null ? null : vm.AssignedToId, ct)).ToSelectList(vm.AssignedToId);
        vm.Customers = (await lookups.CustomersAsync(ct)).ToSelectList(vm.CustomerId?.ToString());
        vm.Leads = (await lookups.LeadsAsync(ct: ct)).ToSelectList(vm.LeadId?.ToString());
        vm.StageOptions = Ui.EnumOptions<OpportunityStage>();
    }
}
