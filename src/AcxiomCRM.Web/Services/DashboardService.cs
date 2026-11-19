using System.Globalization;
using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Domain;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Web.Services;

public interface IDashboardService
{
    Task<DashboardData> GetAsync(DateRangeFilter filter, CancellationToken ct = default);

    /// <summary>Resolves a period filter into a business-local [from, to) range (null = unbounded).</summary>
    (DateTime? From, DateTime? To, string Label) ResolveRange(DateRangeFilter filter);
}

/// <summary>KPI cards and chart series, always computed from server-scoped (authorized) data.</summary>
public sealed class DashboardService(ApplicationDbContext db, IDataScope scope, IBusinessClock clock,
    IOpportunityService opportunities, IFollowUpService followUps, ILeadService leads, IAuditService audit) : IDashboardService
{
    public (DateTime? From, DateTime? To, string Label) ResolveRange(DateRangeFilter filter)
    {
        var today = clock.Today;
        switch (filter.Period?.ToLowerInvariant())
        {
            case "today":
                return (today, today.AddDays(1), "Today");
            case "week":
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                return (monday, monday.AddDays(7), "This Week");
            case "month":
                var first = new DateTime(today.Year, today.Month, 1);
                return (first, first.AddMonths(1), "This Month");
            case "year":
                var jan = new DateTime(today.Year, 1, 1);
                return (jan, jan.AddYears(1), "This Year");
            case "custom":
                var from = filter.From?.Date;
                var to = filter.To?.Date.AddDays(1);
                if (from is { } f && to is { } t && f >= t) (from, to) = (t.AddDays(-1), f.AddDays(1));
                var label = $"{(from is null ? "Beginning" : Fmt.Date(from.Value))} – {(to is null ? "Today" : Fmt.Date(to.Value.AddDays(-1)))}";
                return (from, to, label);
            default:
                return (null, null, "All Time");
        }
    }

    public async Task<DashboardData> GetAsync(DateRangeFilter filter, CancellationToken ct = default)
    {
        var (from, to, label) = ResolveRange(filter);
        DateTime? fromUtc = from is { } f ? clock.ToUtc(f) : null;
        DateTime? toUtc = to is { } t ? clock.ToUtc(t) : null;

        var customers = await scope.ApplyAsync(db.Customers.AsNoTracking(), ct);
        var leadQ = await scope.ApplyAsync(db.Leads.AsNoTracking(), ct);
        var oppQ = await scope.ApplyAsync(db.Opportunities.AsNoTracking(), ct);
        var fuQ = await scope.ApplyAsync(db.FollowUps.AsNoTracking(), ct);

        if (fromUtc is { } fu) { customers = customers.Where(c => c.CreatedDate >= fu); leadQ = leadQ.Where(l => l.CreatedDate >= fu); }
        if (toUtc is { } tu) { customers = customers.Where(c => c.CreatedDate < tu); leadQ = leadQ.Where(l => l.CreatedDate < tu); }

        var leadCounts = await leadQ.GroupBy(l => l.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);

        // Opportunity aggregates are computed from a narrow projection (portable across providers).
        var oppRows = await oppQ.Select(o => new { o.Stage, o.Amount, o.Probability, o.CreatedDate, o.ClosedDate }).ToListAsync(ct);
        bool CreatedInRange(DateTime created) => (fromUtc is null || created >= fromUtc) && (toUtc is null || created < toUtc);
        bool ClosedInRange(DateTime? closed) => closed is { } c && (fromUtc is null || c >= fromUtc) && (toUtc is null || c < toUtc);

        var inRange = oppRows.Where(o => CreatedInRange(o.CreatedDate)).ToList();
        var open = inRange.Where(o => OpportunityRules.IsActive(o.Stage)).ToList();
        var won = oppRows.Where(o => o.Stage == OpportunityStage.Won && ClosedInRange(o.ClosedDate)).ToList();
        var lost = oppRows.Where(o => o.Stage == OpportunityStage.Lost && ClosedInRange(o.ClosedDate)).ToList();

        var now = clock.Now;
        var pendingFollowUps = await fuQ.CountAsync(x => x.Status == FollowUpStatus.Planned, ct);
        var overdueFollowUps = await fuQ.CountAsync(x => x.Status == FollowUpStatus.Planned && x.FollowUpDate < now, ct);

        var pipeline = Enum.GetValues<OpportunityStage>().Select(s =>
        {
            var rows = s is OpportunityStage.Won or OpportunityStage.Lost
                ? oppRows.Where(o => o.Stage == s && ClosedInRange(o.ClosedDate)).ToList()
                : open.Where(o => o.Stage == s).ToList();
            return new PipelineStageDto(s, rows.Count, rows.Sum(r => r.Amount), rows.Sum(r => OpportunityRules.Weighted(r.Amount, r.Probability)));
        }).ToList();

        var data = new DashboardData
        {
            ScopeDescription = scope.Description,
            PeriodLabel = label,
            Filter = filter,
            TotalCustomers = await customers.CountAsync(ct),
            TotalLeads = leadCounts.Sum(x => x.Count),
            OpenLeads = leadCounts.Where(x => LeadWorkflow.OpenStatuses.Contains(x.Status)).Sum(x => x.Count),
            TotalOpportunities = inRange.Count,
            OpenOpportunities = open.Count,
            WonOpportunities = won.Count,
            LostOpportunities = lost.Count,
            TotalPipelineValue = open.Sum(o => o.Amount),
            WeightedPipelineValue = open.Sum(o => OpportunityRules.Weighted(o.Amount, o.Probability)),
            WonAmount = won.Sum(o => o.Amount),
            PendingFollowUps = pendingFollowUps,
            OverdueFollowUps = overdueFollowUps,
            LeadStatus = Enum.GetValues<LeadStatus>()
                .Select(s => new ChartPoint(s.ToString(), leadCounts.FirstOrDefault(x => x.Status == s)?.Count ?? 0)).ToList(),
            Pipeline = pipeline,
            MonthlySales = BuildMonthly(oppRows.Select(o => (o.Stage, o.Amount, o.ClosedDate)).ToList(), from, to),
            UpcomingFollowUps = (await followUps.SearchAsync(new FollowUpQuery { View = "upcoming", PageSize = 6 }, ct)).Items,
            OverdueList = (await followUps.SearchAsync(new FollowUpQuery { View = "overdue", PageSize = 6 }, ct)).Items,
            RecentLeads = scope.IsSalesExecutive ? (await leads.SearchAsync(new LeadQuery { PageSize = 5 }, ct)).Items : [],
            TeamPerformance = scope.IsSalesExecutive ? [] : (await opportunities.PipelineAsync(ct: ct)).Owners
        };

        if (!scope.IsAdmin) return data;

        var nowUtc = clock.UtcNow;
        var dayAgo = nowUtc.AddDays(-1);
        var users = await db.Users.AsNoTracking().Select(u => new { u.IsActive, u.LockoutEnd }).ToListAsync(ct);
        var nowOffset = DateTimeOffset.UtcNow;
        data.Users = new UserStats(users.Count, users.Count(u => u.IsActive), users.Count(u => !u.IsActive),
            users.Count(u => u.LockoutEnd is { } le && le > nowOffset));
        data.FailedLogins24h = await db.AuditLogs.CountAsync(a => a.Action == AuditAction.LoginFailed && a.CreatedDate >= dayAgo, ct);
        data.Lockouts24h = await db.AuditLogs.CountAsync(a => a.Action == AuditAction.Lockout && a.CreatedDate >= dayAgo, ct);
        data.RecentAudit = (await audit.SearchAsync(new AuditQuery { PageSize = 8 }, ct)).Items;
        return data;
    }

    private List<MonthlySalesPoint> BuildMonthly(List<(OpportunityStage Stage, decimal Amount, DateTime? ClosedDate)> rows, DateTime? from, DateTime? to)
    {
        var today = clock.Today;
        var end = to is { } t ? new DateTime(t.AddDays(-1).Year, t.AddDays(-1).Month, 1) : new DateTime(today.Year, today.Month, 1);
        var start = from is { } f ? new DateTime(f.Year, f.Month, 1) : end.AddMonths(-11);
        if (start > end) start = end;
        if ((end.Year - start.Year) * 12 + end.Month - start.Month > 23) start = end.AddMonths(-23);

        var closed = rows.Where(r => r.ClosedDate.HasValue)
            .Select(r => (r.Stage, r.Amount, Local: clock.ToLocal(r.ClosedDate!.Value))).ToList();

        var result = new List<MonthlySalesPoint>();
        for (var m = start; m <= end; m = m.AddMonths(1))
        {
            var inMonth = closed.Where(c => c.Local.Year == m.Year && c.Local.Month == m.Month).ToList();
            result.Add(new MonthlySalesPoint(m.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                inMonth.Where(c => c.Stage == OpportunityStage.Won).Sum(c => c.Amount),
                inMonth.Count(c => c.Stage == OpportunityStage.Won),
                inMonth.Count(c => c.Stage == OpportunityStage.Lost)));
        }
        return result;
    }
}
