using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Services;
using AcxiomCRM.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Web.Controllers;

[Authorize(Policy = Policies.CrmUser)]
public sealed class FollowUpsController(
    IFollowUpService followUps,
    IAuditService audit,
    ILookupService lookups,
    IBusinessClock clock,
    IDataScope scope) : CrmController
{
    /// <summary>Follow-up list. view = pending | overdue | today | upcoming | (all).</summary>
    public async Task<IActionResult> Index([FromQuery] FollowUpQuery query, CancellationToken ct)
    {
        if (query.RelatedType is null && Request.Query.ContainsKey("related"))
            (query.RelatedType, query.RelatedId) = Ui.ParseRelatedKey(Request.Query["related"]);
        ViewData["RelatedOptions"] = (await lookups.RelatedRecordsAsync(ct: ct)).ToSelectList(Ui.RelatedKey(query.RelatedType, query.RelatedId));
        return View(new ListPageViewModel<FollowUpDto, FollowUpQuery>
        {
            Query = query,
            Results = await followUps.SearchAsync(query, ct),
            Owners = (await lookups.VisibleUsersAsync(ct)).ToSelectList(query.AssignedToId),
            CanDelete = scope.CanDelete,
            Scope = scope.Description
        });
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await followUps.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        return View(new FollowUpDetailsViewModel
        {
            FollowUp = result.Value!,
            History = await audit.ForRecordAsync("FollowUp", id.ToString(), ct: ct),
            Reschedule = new FollowUpRescheduleRequest { NewDate = clock.Today.AddDays(1).AddHours(11) },
            CanDelete = scope.CanDelete
        });
    }

    /// <summary>Schedule a follow-up. relatedKey pre-selects the record, e.g. "Customer:12".</summary>
    public async Task<IActionResult> Create(string? relatedKey, CancellationToken ct)
    {
        var vm = new FollowUpFormViewModel
        {
            RelatedKey = relatedKey,
            AssignedToId = scope.UserId,
            FollowUpDate = clock.Now.Date.AddDays(1).AddHours(11)
        };
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FollowUpFormViewModel vm, CancellationToken ct)
    {
        (vm.RelatedType, vm.RelatedId) = Ui.ParseRelatedKey(vm.RelatedKey);
        if (ModelState.IsValid)
        {
            var result = await followUps.CreateAsync(vm, ct);
            if (result.Succeeded)
            {
                Success("Follow-up scheduled.");
                return RedirectToAction(nameof(Details), new { id = result.Value!.FollowUpId });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result, MapKey);
        }
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await followUps.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        var f = result.Value!;
        var vm = new FollowUpFormViewModel
        {
            FollowUpId = f.FollowUpId, Subject = f.Subject, FollowUpType = f.FollowUpType, FollowUpDate = f.FollowUpDate, Remarks = f.Remarks,
            RelatedKey = Ui.RelatedKey(f.RelatedType, f.RelatedId), AssignedToId = f.AssignedToId
        };
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FollowUpFormViewModel vm, CancellationToken ct)
    {
        vm.FollowUpId = id;
        (vm.RelatedType, vm.RelatedId) = Ui.ParseRelatedKey(vm.RelatedKey);
        ModelState.Remove(nameof(vm.FollowUpDate)); // the date changes only through Reschedule
        if (ModelState.IsValid)
        {
            var result = await followUps.UpdateAsync(id, vm, ct);
            if (result.Succeeded)
            {
                Success("Follow-up updated.");
                return RedirectToAction(nameof(Details), new { id });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result, MapKey);
        }
        var current = await followUps.GetAsync(id, ct);
        vm.FollowUpDate = current.Value?.FollowUpDate;
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id, [Bind(Prefix = "Complete")] FollowUpCompleteRequest request, CancellationToken ct) =>
        Outcome(id, await followUps.CompleteAsync(id, request, ct), "Follow-up marked as completed.");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule(int id, [Bind(Prefix = "Reschedule")] FollowUpRescheduleRequest request, CancellationToken ct) =>
        Outcome(id, await followUps.RescheduleAsync(id, request, ct), "Follow-up rescheduled.");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Missed(int id, CancellationToken ct) =>
        Outcome(id, await followUps.MarkMissedAsync(id, ct), "Follow-up marked as missed.");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        Outcome(id, await followUps.CancelAsync(id, ct), "Follow-up cancelled.");

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policies.CanDeleteRecords)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await followUps.DeleteAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        Success("Follow-up deleted.");
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Outcome(int id, ServiceResult result, string success)
    {
        if (Deny(result) is { } denied) return denied;
        if (result.Succeeded) Success(success);
        else Failure(result.Message ?? "The action could not be completed.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private static string MapKey(string key) => key is nameof(FollowUpEditRequest.RelatedId) or nameof(FollowUpEditRequest.RelatedType)
        ? nameof(FollowUpFormViewModel.RelatedKey) : key;

    private async Task PopulateAsync(FollowUpFormViewModel vm, CancellationToken ct)
    {
        vm.Owners = (await lookups.AssignableUsersAsync(vm.FollowUpId is null ? null : vm.AssignedToId, ct)).ToSelectList(vm.AssignedToId);
        vm.RelatedOptions = (await lookups.RelatedRecordsAsync(ct: ct)).ToSelectList(vm.RelatedKey);
    }
}
