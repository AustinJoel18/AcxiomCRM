using System.Globalization;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace AcxiomCRM.Web.Infrastructure;

public static class Ui
{
    /// <summary>Bootstrap badge classes for statuses/stages.</summary>
    public static string BadgeClass(object? value) => value?.ToString() switch
    {
        "New" or "Planned" or "Qualification" or "Prospect" => "badge-soft-info",
        "Contacted" or "Proposal" or "Medium" => "badge-soft-primary",
        "Qualified" or "Negotiation" => "badge-soft-warning",
        "Converted" or "Won" or "Completed" or "Active" or "Success" or "Open" => "badge-soft-success",
        "Lost" or "Missed" or "Failure" or "High" or "Overdue" => "badge-soft-danger",
        "Unqualified" or "Cancelled" or "Inactive" or "Low" => "badge-soft-secondary",
        _ => "badge-soft-secondary"
    };

    public static IHtmlContent Badge(object? value, string? text = null) =>
        new HtmlString($"<span class=\"badge {BadgeClass(value)}\">{System.Net.WebUtility.HtmlEncode(text ?? Fmt.Humanize(value))}</span>");

    public static string Icon(RelatedRecordType? type) => type switch
    {
        RelatedRecordType.Customer => "bi-building",
        RelatedRecordType.Lead => "bi-person-lines-fill",
        RelatedRecordType.Opportunity => "bi-briefcase",
        _ => "bi-dash"
    };

    public static string Icon(ActivityType type) => type switch
    {
        ActivityType.Call => "bi-telephone",
        ActivityType.Meeting => "bi-people",
        ActivityType.Email => "bi-envelope",
        _ => "bi-check2-square"
    };

    public static string Icon(FollowUpType type) => type switch
    {
        FollowUpType.Call => "bi-telephone",
        FollowUpType.Meeting => "bi-people",
        FollowUpType.Email => "bi-envelope",
        FollowUpType.Visit => "bi-geo-alt",
        _ => "bi-check2-square"
    };

    public static string RelatedController(RelatedRecordType? type) => type switch
    {
        RelatedRecordType.Customer => "Customers",
        RelatedRecordType.Lead => "Leads",
        _ => "Opportunities"
    };

    public static IEnumerable<SelectListItem> ToSelectList(this IEnumerable<OptionDto> options, string? selected = null)
    {
        var groups = new Dictionary<string, SelectListGroup>();
        foreach (var o in options)
        {
            SelectListGroup? group = null;
            if (o.Group is not null && !groups.TryGetValue(o.Group, out group))
                groups[o.Group] = group = new SelectListGroup { Name = o.Group };
            yield return new SelectListItem(o.Text, o.Value, o.Value == selected) { Group = group };
        }
    }

    public static IEnumerable<SelectListItem> EnumOptions<TEnum>(IEnumerable<TEnum>? only = null) where TEnum : struct, Enum =>
        (only ?? Enum.GetValues<TEnum>()).Select(e => new SelectListItem(Fmt.Humanize(e), e.ToString()));

    /// <summary>Enum options with the current filter value pre-selected (for selects without asp-for).</summary>
    public static IEnumerable<SelectListItem> EnumOptions<TEnum>(TEnum? selected) where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(e => new SelectListItem(Fmt.Humanize(e), e.ToString(), selected.HasValue && selected.Value.Equals(e)));

    public static IEnumerable<SelectListItem> Options(IEnumerable<OptionDto> options, string? selected) =>
        options.Select(o => new SelectListItem(o.Text, o.Value, o.Value == selected));

    public static string? RelatedKey(RelatedRecordType? type, int? id) => type is null || id is null ? null : $"{type}:{id}";

    public static (RelatedRecordType? Type, int? Id) ParseRelatedKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return (null, null);
        var parts = key.Split(':', 2);
        return parts.Length == 2 && Enum.TryParse<RelatedRecordType>(parts[0], out var t) && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? (t, id)
            : (null, -1);
    }

    /// <summary>Copies service-layer errors into ModelState (optionally re-mapping keys for the form).</summary>
    public static void AddServiceErrors(this ModelStateDictionary modelState, ServiceResult result, Func<string, string>? map = null)
    {
        foreach (var (key, messages) in result.Errors)
            foreach (var m in messages)
                modelState.AddModelError(map?.Invoke(key) ?? key, m);
    }
}

/// <summary>&lt;a crm-sort="name"&gt;Name&lt;/a&gt; → sortable column header preserving the current query string.</summary>
[HtmlTargetElement("a", Attributes = "crm-sort")]
public sealed class SortLinkTagHelper : TagHelper
{
    [HtmlAttributeName("crm-sort")] public string Key { get; set; } = "";

    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var request = ViewContext.HttpContext.Request;
        var query = request.Query.ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var isCurrent = query.TryGetValue("sort", out var sort) && string.Equals(sort, Key, StringComparison.OrdinalIgnoreCase);
        var isDesc = isCurrent && query.TryGetValue("dir", out var dir) && dir == "desc";
        query["sort"] = Key;
        query["dir"] = isCurrent && !isDesc ? "desc" : "asc";
        query["page"] = "1";

        output.Attributes.SetAttribute("href", request.Path + QueryString.Create(query!));
        output.Attributes.SetAttribute("class", "sort-link" + (isCurrent ? " active" : ""));
        output.PostContent.AppendHtml(isCurrent
            ? $" <i class=\"bi {(isDesc ? "bi-sort-down" : "bi-sort-up")}\" aria-hidden=\"true\"></i>"
            : " <i class=\"bi bi-arrow-down-up opacity-25\" aria-hidden=\"true\"></i>");
        output.Attributes.SetAttribute("aria-sort", isCurrent ? (isDesc ? "descending" : "ascending") : "none");
    }
}

/// <summary>&lt;crm-pager page="1" page-size="10" total-count="57" /&gt; → Bootstrap pagination preserving filters.</summary>
[HtmlTargetElement("crm-pager", TagStructure = TagStructure.WithoutEndTag)]
public sealed class PagerTagHelper : TagHelper
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }

    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "nav";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("aria-label", "Pagination");
        output.Attributes.SetAttribute("class", "d-flex flex-wrap align-items-center justify-content-between gap-2 mt-3");

        var totalPages = PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        var first = TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
        var last = Math.Min(TotalCount, Page * PageSize);
        var html = new System.Text.StringBuilder();
        html.Append($"<div class=\"text-body-secondary small\">Showing {first}–{last} of {TotalCount:N0}</div>");

        if (totalPages > 1)
        {
            var request = ViewContext.HttpContext.Request;
            string Link(int p)
            {
                var q = request.Query.ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                q["page"] = p.ToString(CultureInfo.InvariantCulture);
                return System.Net.WebUtility.HtmlEncode(request.Path + QueryString.Create(q!).ToString());
            }
            void Item(int p, string label, bool disabled = false, bool active = false, string? aria = null) =>
                html.Append($"<li class=\"page-item{(disabled ? " disabled" : "")}{(active ? " active" : "")}\">" +
                            (disabled || active
                                ? $"<span class=\"page-link\"{(active ? " aria-current=\"page\"" : "")}>{label}</span>"
                                : $"<a class=\"page-link\" href=\"{Link(p)}\"{(aria is null ? "" : $" aria-label=\"{aria}\"")}>{label}</a>") + "</li>");

            html.Append("<ul class=\"pagination pagination-sm mb-0\">");
            Item(Page - 1, "&laquo;", Page <= 1, aria: "Previous");
            var start = Math.Max(1, Page - 2);
            var end = Math.Min(totalPages, start + 4);
            start = Math.Max(1, end - 4);
            if (start > 1) { Item(1, "1"); if (start > 2) Item(0, "…", disabled: true); }
            for (var p = start; p <= end; p++) Item(p, p.ToString(CultureInfo.InvariantCulture), active: p == Page);
            if (end < totalPages) { if (end < totalPages - 1) Item(0, "…", disabled: true); Item(totalPages, totalPages.ToString(CultureInfo.InvariantCulture)); }
            Item(Page + 1, "&raquo;", Page >= totalPages, aria: "Next");
            html.Append("</ul>");
        }
        output.Content.SetHtmlContent(html.ToString());
    }
}

/// <summary>Base class for MVC controllers: flash messages and service-result handling.</summary>
public abstract class CrmController : Controller
{
    protected void Success(string message) => TempData["Success"] = message;
    protected void Failure(string message) => TempData["Error"] = message;

    /// <summary>Maps NotFound/Forbidden service results to HTTP results; returns null for Success/Invalid/Conflict.</summary>
    protected IActionResult? Deny(ServiceResult result) => result.Status switch
    {
        ResultStatus.NotFound => NotFound(),
        ResultStatus.Forbidden => Forbid(),
        _ => null
    };
}
