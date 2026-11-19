using System.Diagnostics;
using System.Text.Json;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Services;
using AcxiomCRM.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace AcxiomCRM.Web.Controllers;

public sealed class HomeController(IDashboardService dashboard, IDataScope scope, ILogger<HomeController> logger) : CrmController
{
    /// <summary>Role-based dashboard (KPI cards + Chart.js charts) computed from authorized data only.</summary>
    [Authorize(Policy = Policies.CrmUser)]
    public async Task<IActionResult> Index(DateRangeFilter filter, CancellationToken ct)
    {
        var data = await dashboard.GetAsync(filter, ct);
        var chart = new
        {
            leadStatus = new { labels = data.LeadStatus.Select(p => p.Label), values = data.LeadStatus.Select(p => p.Value) },
            pipeline = new
            {
                labels = data.Pipeline.Select(p => p.Stage.ToString()),
                counts = data.Pipeline.Select(p => p.Count),
                amounts = data.Pipeline.Select(p => p.Amount)
            },
            monthly = new
            {
                labels = data.MonthlySales.Select(m => m.Month),
                won = data.MonthlySales.Select(m => m.WonAmount),
                wonCount = data.MonthlySales.Select(m => m.WonCount),
                lostCount = data.MonthlySales.Select(m => m.LostCount)
            }
        };
        return View(new DashboardViewModel
        {
            Data = data,
            ChartJson = JsonSerializer.Serialize(chart, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            IsAdmin = scope.IsAdmin,
            IsManager = scope.IsManager,
            IsSales = scope.IsSalesExecutive
        });
    }

    /// <summary>Global exception handler target. API callers receive RFC 7807 JSON without internals.</summary>
    [AllowAnonymous, IgnoreAntiforgeryToken]
    public IActionResult Error()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (feature?.Error is { } ex)
            logger.LogError(ex, "Unhandled exception for {Path}", feature.Path);

        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        if (feature?.Path?.StartsWith("/api", StringComparison.OrdinalIgnoreCase) == true)
            return Problem(statusCode: 500, title: "An unexpected error occurred.", detail: $"Reference: {traceId}");

        Response.StatusCode = 500;
        return View(new ErrorViewModel { RequestId = traceId });
    }

    /// <summary>Friendly status pages (404, 400...) for MVC; ProblemDetails for API paths.</summary>
    [AllowAnonymous, IgnoreAntiforgeryToken]
    public new IActionResult StatusCode(int code)
    {
        var original = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        var reason = ReasonPhrases.GetReasonPhrase(code);
        if (original?.OriginalPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase) == true)
        {
            var title = code switch
            {
                401 => "Authentication is required to access this resource.",
                403 => "You do not have permission to access this resource.",
                404 => "The requested resource was not found.",
                _ => string.IsNullOrEmpty(reason) ? "Request failed." : reason
            };
            return Problem(statusCode: code, title: title);
        }

        Response.StatusCode = code;
        return View("Error", new ErrorViewModel
        {
            StatusCode = code,
            Title = code == 404 ? "Page not found" : string.IsNullOrEmpty(reason) ? "Request failed" : reason,
            Message = code == 404
                ? "The page or record you are looking for does not exist or has been removed."
                : "The request could not be completed."
        });
    }
}
