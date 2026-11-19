using System.Text.Json;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Services;
using AcxiomCRM.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Web.Controllers;

/// <summary>Reports with filters, sorting, pagination and CSV export; data visibility follows the caller's role scope.</summary>
[Authorize(Policy = Policies.CrmUser)]
public sealed class ReportsController(
    IReportService reports,
    IOpportunityService opportunities,
    ILookupService lookups,
    IAuditService audit,
    IBusinessClock clock,
    IDataScope scope) : CrmController
{
    public IActionResult Index()
    {
        var list = new List<ReportLink>
        {
            new(nameof(Customers), "Customer Report", "Customer list, status, owner and creation date.", "bi-building", "All roles (scoped)"),
            new(nameof(Leads), "Lead Report", "Lead source, status, owner and conversion details.", "bi-person-lines-fill", "All roles (scoped)"),
            new(nameof(FollowUps), "Follow-Up Report", "Planned, completed, missed and overdue activities.", "bi-calendar-check", "All roles (scoped)"),
            new(nameof(Opportunities), "Opportunity Report", "Stage, amount, probability and expected close date.", "bi-briefcase", "All roles (scoped)"),
            new(nameof(Pipeline), "Pipeline Report", "Stage-wise and owner-wise weighted pipeline.", "bi-funnel", "Admin / Manager; scoped sales view"),
            new(nameof(Conversion), "Sales / Conversion Report", "Lead conversion and opportunity outcome metrics.", "bi-graph-up-arrow", "Admin / Manager; scoped sales view")
        };
        if (!scope.IsSalesExecutive)
            list.Add(new(nameof(UserActivity), "User Activity Report", "Actions and activity counts per user.", "bi-activity", "Admin / Manager (team)"));
        if (scope.IsAdmin)
            list.Add(new(nameof(Audit), "Audit Report", "Security and business audit events.", "bi-shield-lock", "Admin only"));
        return View(list);
    }

    public Task<IActionResult> Customers([FromQuery] ReportFilter f, string? export, CancellationToken ct) => Table(f, export, reports.CustomersAsync, ct);
    public Task<IActionResult> Leads([FromQuery] ReportFilter f, string? export, CancellationToken ct) => Table(f, export, reports.LeadsAsync, ct);
    public Task<IActionResult> FollowUps([FromQuery] ReportFilter f, string? export, CancellationToken ct) => Table(f, export, reports.FollowUpsAsync, ct);
    public Task<IActionResult> Opportunities([FromQuery] ReportFilter f, string? export, CancellationToken ct) => Table(f, export, reports.OpportunitiesAsync, ct);

    [Authorize(Policy = Policies.ManagerOrAdmin)]
    public Task<IActionResult> UserActivity([FromQuery] ReportFilter f, string? export, CancellationToken ct) => Table(f, export, reports.UserActivityAsync, ct);

    [Authorize(Policy = Policies.AdminOnly)]
    public Task<IActionResult> Audit([FromQuery] ReportFilter f, string? export, CancellationToken ct) => Table(f, export, reports.AuditAsync, ct);

    public async Task<IActionResult> Pipeline([FromQuery] ReportFilter f, CancellationToken ct)
    {
        f.Normalize();
        var report = await opportunities.PipelineAsync(f.OwnerId, f.From, f.To, ct);
        var chart = JsonSerializer.Serialize(new
        {
            labels = report.Stages.Select(s => s.Stage.ToString()),
            amounts = report.Stages.Select(s => s.Amount),
            weighted = report.Stages.Select(s => s.WeightedAmount)
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return View(new PipelineReportViewModel { Report = report, Filter = f, Owners = await OwnersAsync(f, ct), ChartJson = chart });
    }

    public async Task<IActionResult> Conversion([FromQuery] ReportFilter f, CancellationToken ct)
    {
        f.Normalize();
        return View(new ConversionReportViewModel { Report = await reports.ConversionAsync(f, ct), Filter = f, Owners = await OwnersAsync(f, ct) });
    }

    private async Task<IActionResult> Table(ReportFilter f, string? export, Func<ReportFilter, CancellationToken, Task<ReportTable>> build, CancellationToken ct)
    {
        if (string.Equals(export, "csv", StringComparison.OrdinalIgnoreCase))
        {
            f.Page = 1;
            f.PageSize = int.MaxValue;
            var all = await build(f, ct);
            await audit.LogAsync(AuditModule.Report, AuditAction.Export, details: $"{all.Title} exported ({all.TotalCount} rows).", ct: ct);
            return File(ReportService.ToCsv(all), "text/csv", $"{all.Key}-report-{clock.Today:yyyyMMdd}.csv");
        }
        f.Normalize();
        var table = await build(f, ct);
        return View("Table", new ReportPageViewModel { Table = table, Filter = f, Owners = await OwnersAsync(f, ct) });
    }

    private async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> OwnersAsync(ReportFilter f, CancellationToken ct) =>
        (await lookups.VisibleUsersAsync(ct)).ToSelectList(f.OwnerId);
}
