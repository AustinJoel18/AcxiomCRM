using AcxiomCRM.Web.Models;

namespace AcxiomCRM.Web.Dtos;

public sealed class DateRangeFilter
{
    /// <summary>all | today | week | month | year | custom</summary>
    public string? Period { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed record ChartPoint(string Label, decimal Value);

public sealed record MonthlySalesPoint(string Month, decimal WonAmount, int WonCount, int LostCount);

public sealed record UserStats(int Total, int Active, int Inactive, int LockedOut);

public sealed class DashboardData
{
    public string ScopeDescription { get; init; } = "";
    public string PeriodLabel { get; init; } = "";
    public DateRangeFilter Filter { get; init; } = new();

    public int TotalCustomers { get; init; }
    public int TotalLeads { get; init; }
    public int OpenLeads { get; init; }
    public int TotalOpportunities { get; init; }
    public int OpenOpportunities { get; init; }
    public int WonOpportunities { get; init; }
    public int LostOpportunities { get; init; }
    public decimal TotalPipelineValue { get; init; }
    public decimal WeightedPipelineValue { get; init; }
    public decimal WonAmount { get; init; }
    public int PendingFollowUps { get; init; }
    public int OverdueFollowUps { get; init; }

    public IReadOnlyList<ChartPoint> LeadStatus { get; init; } = [];
    public IReadOnlyList<PipelineStageDto> Pipeline { get; init; } = [];
    public IReadOnlyList<MonthlySalesPoint> MonthlySales { get; init; } = [];

    public IReadOnlyList<FollowUpDto> UpcomingFollowUps { get; init; } = [];
    public IReadOnlyList<FollowUpDto> OverdueList { get; init; } = [];
    public IReadOnlyList<PipelineOwnerDto> TeamPerformance { get; init; } = [];
    public IReadOnlyList<LeadDto> RecentLeads { get; init; } = [];

    public UserStats? Users { get; set; }
    public int FailedLogins24h { get; set; }
    public int Lockouts24h { get; set; }
    public IReadOnlyList<AuditLogDto> RecentAudit { get; set; } = [];
}

public sealed class ReportFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Status { get; set; }
    public string? OwnerId { get; set; }
    public string? Search { get; set; }
    public string? Sort { get; set; }
    public string? Dir { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public bool Descending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);

    public void Normalize(int maxPageSize = 100)
    {
        if (Page < 1) Page = 1;
        if (PageSize < 1) PageSize = 25;
        if (PageSize > maxPageSize) PageSize = maxPageSize;
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        if (From is { } f && To is { } t && f > t) (From, To) = (t, f);
    }
}

public sealed record ReportColumn(string Key, string Header, bool Sortable = true, bool Numeric = false);

/// <summary>Generic tabular report (rendered as an HTML table or exported as CSV).</summary>
public sealed class ReportTable
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public required IReadOnlyList<ReportColumn> Columns { get; init; }
    public required IReadOnlyList<string[]> Rows { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalCount { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> Summary { get; init; } = [];
    public IReadOnlyList<OptionDto> StatusOptions { get; init; } = [];
    public string StatusLabel { get; init; } = "Status";
    public bool ShowOwnerFilter { get; init; } = true;
    public string DateLabel { get; init; } = "Created";
    public string Scope { get; init; } = "";
}

public sealed record ConversionBreakdown(string Label, int Total, int Converted, int Lost, double ConversionRate);

public sealed class ConversionReportDto
{
    public int TotalLeads { get; init; }
    public int ConvertedLeads { get; init; }
    public int LostLeads { get; init; }
    public int UnqualifiedLeads { get; init; }
    public int OpenLeads { get; init; }
    public double ConversionRate { get; init; }
    public int WonOpportunities { get; init; }
    public int LostOpportunities { get; init; }
    public double WinRate { get; init; }
    public decimal WonAmount { get; init; }
    public decimal AverageDealSize { get; init; }
    public IReadOnlyList<ConversionBreakdown> BySource { get; init; } = [];
    public IReadOnlyList<ConversionBreakdown> ByOwner { get; init; } = [];
    public string Scope { get; init; } = "";
}
