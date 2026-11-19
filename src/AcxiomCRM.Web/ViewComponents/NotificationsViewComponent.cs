using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Web.ViewComponents;

/// <summary>Header bell: overdue and due-today follow-ups assigned to the signed-in user (reminders).</summary>
public sealed class NotificationsViewComponent(IFollowUpService followUps, ICurrentUser currentUser) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!currentUser.IsAuthenticated || !AppRoles.All.Any(currentUser.IsInRole))
            return Content(string.Empty);
        var (overdue, dueToday, items) = await followUps.RemindersForCurrentUserAsync();
        ViewData["Overdue"] = overdue;
        ViewData["DueToday"] = dueToday;
        return View(items);
    }
}
