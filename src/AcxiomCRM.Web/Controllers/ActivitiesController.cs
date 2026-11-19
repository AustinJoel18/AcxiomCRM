using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Services;
using AcxiomCRM.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Web.Controllers;

/// <summary>Activity management: calls, meetings, emails and tasks.</summary>
[Authorize(Policy = Policies.CrmUser)]
public sealed class ActivitiesController(
    IActivityService activities,
    IAuditService audit,
    ILookupService lookups,
    IBusinessClock clock,
    IDataScope scope) : CrmController
{
    public async Task<IActionResult> Index([FromQuery] ActivityQuery query, CancellationToken ct) =>
        View(new ListPageViewModel<ActivityDto, ActivityQuery>
        {
            Query = query,
            Results = await activities.SearchAsync(query, ct: ct),
            Owners = (await lookups.VisibleUsersAsync(ct)).ToSelectList(query.AssignedToId),
            CanDelete = true,
            Scope = scope.Description
        });

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await activities.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        return View(new ActivityDetailsViewModel
        {
            Activity = result.Value!,
            History = await audit.ForRecordAsync("Activity", id.ToString(), ct: ct),
            CanDelete = scope.CanDelete || result.Value!.AssignedToId == scope.UserId
        });
    }

    public async Task<IActionResult> Create(string? relatedKey, ActivityType? type, CancellationToken ct)
    {
        var vm = new ActivityFormViewModel
        {
            RelatedKey = relatedKey, ActivityType = type ?? ActivityType.Call, ActivityDate = clock.Now.AddMinutes(-clock.Now.Minute),
            Status = ActivityStatus.Planned, AssignedToId = scope.UserId
        };
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ActivityFormViewModel vm, CancellationToken ct)
    {
        (vm.RelatedType, vm.RelatedId) = Ui.ParseRelatedKey(vm.RelatedKey);
        if (ModelState.IsValid)
        {
            var result = await activities.CreateAsync(vm, ct);
            if (result.Succeeded)
            {
                Success("Activity logged.");
                return RedirectToAction(nameof(Details), new { id = result.Value!.ActivityId });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result, MapKey);
        }
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await activities.GetAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        var a = result.Value!;
        var vm = new ActivityFormViewModel
        {
            ActivityId = a.ActivityId, ActivityType = a.ActivityType, Subject = a.Subject, Description = a.Description, ActivityDate = a.ActivityDate,
            Status = a.Status, RelatedKey = Ui.RelatedKey(a.RelatedType, a.RelatedId), AssignedToId = a.AssignedToId
        };
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ActivityFormViewModel vm, CancellationToken ct)
    {
        vm.ActivityId = id;
        (vm.RelatedType, vm.RelatedId) = Ui.ParseRelatedKey(vm.RelatedKey);
        if (ModelState.IsValid)
        {
            var result = await activities.UpdateAsync(id, vm, ct);
            if (result.Succeeded)
            {
                Success("Activity updated.");
                return RedirectToAction(nameof(Details), new { id });
            }
            if (Deny(result) is { } denied) return denied;
            ModelState.AddServiceErrors(result, MapKey);
        }
        await PopulateAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await activities.DeleteAsync(id, ct);
        if (Deny(result) is { } denied) return denied;
        Success("Activity deleted.");
        return RedirectToAction(nameof(Index));
    }

    private static string MapKey(string key) => key is nameof(ActivitySaveRequest.RelatedId) or nameof(ActivitySaveRequest.RelatedType)
        ? nameof(ActivityFormViewModel.RelatedKey) : key;

    private async Task PopulateAsync(ActivityFormViewModel vm, CancellationToken ct)
    {
        vm.Owners = (await lookups.AssignableUsersAsync(vm.ActivityId is null ? null : vm.AssignedToId, ct)).ToSelectList(vm.AssignedToId);
        vm.RelatedOptions = (await lookups.RelatedRecordsAsync(ct: ct)).ToSelectList(vm.RelatedKey);
    }
}
