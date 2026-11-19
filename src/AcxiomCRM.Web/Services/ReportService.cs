using System.Text;
using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Domain;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Web.Services;

public interface IReportService
{
    Task<ReportTable> CustomersAsync(ReportFilter f, CancellationToken ct = default);
    Task<ReportTable> LeadsAsync(ReportFilter f, CancellationToken ct = default);
    Task<ReportTable> FollowUpsAsync(ReportFilter f, CancellationToken ct = default);
    Task<ReportTable> OpportunitiesAsync(ReportFilter f, CancellationToken ct = default);
    Task<ReportTable> UserActivityAsync(ReportFilter f, CancellationToken ct = default);
    Task<ReportTable> AuditAsync(ReportFilter f, CancellationToken ct = default);
    Task<ConversionReportDto> ConversionAsync(ReportFilter f, CancellationToken ct = default);
}

/// <summary>
/// Reporting (read-only). Every report starts from a data-scoped query, so Sales Executives only
/// ever see their own records, Managers their team, and Admins everything.
/// </summary>
public sealed class ReportService(ApplicationDbContext db, IDataScope scope, IBusinessClock clock, IAuditService audit) : IReportService
{
    public async Task<ReportTable> CustomersAsync(ReportFilter f, CancellationToken ct = default)
    {
        f.Normalize(int.MaxValue);
        var q = await scope.ApplyAsync(db.Customers.AsNoTracking(), ct);
        q = ApplyCreatedRange(q, f, c => c.CreatedDate);
        if (Enum.TryParse<CustomerStatus>(f.Status, out var st)) q = q.Where(c => c.Status == st);
        if (!string.IsNullOrEmpty(f.OwnerId)) q = q.Where(c => c.AssignedToId == f.OwnerId);
        if (f.Search is { } s)
        {
            var like = $"%{s}%";
            q = q.Where(c => EF.Functions.Like(c.CustomerName, like) || EF.Functions.Like(c.Email, like) || (c.CompanyName != null && EF.Functions.Like(c.CompanyName, like)));
        }

        var statusCounts = await q.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var d = f.Descending;
        q = f.Sort switch
        {
            "code" => d ? q.OrderByDescending(c => c.CustomerCode) : q.OrderBy(c => c.CustomerCode),
            "name" => d ? q.OrderByDescending(c => c.CustomerName) : q.OrderBy(c => c.CustomerName),
            "company" => d ? q.OrderByDescending(c => c.CompanyName) : q.OrderBy(c => c.CompanyName),
            "status" => d ? q.OrderByDescending(c => c.Status) : q.OrderBy(c => c.Status),
            "owner" => d ? q.OrderByDescending(c => c.AssignedTo!.FullName) : q.OrderBy(c => c.AssignedTo!.FullName),
            "created" when !d => q.OrderBy(c => c.CreatedDate),
            _ => q.OrderByDescending(c => c.CreatedDate)
        };
        var total = await q.CountAsync(ct);
        var rows = await Page(q, f).Select(c => new { c.CustomerCode, c.CustomerName, c.CompanyName, c.Email, c.Phone, c.City, c.Status, Owner = c.AssignedTo!.FullName, c.CreatedDate }).ToListAsync(ct);

        return new ReportTable
        {
            Key = "customers", Title = "Customer Report", Description = "Customer list with status, owner and creation date.",
            Columns = [new("code", "Code"), new("name", "Customer"), new("company", "Company"), new("email", "Email", false), new("phone", "Phone", false),
                new("city", "City", false), new("status", "Status"), new("owner", "Owner"), new("created", "Created")],
            Rows = rows.Select(r => new[] { r.CustomerCode, r.CustomerName, r.CompanyName ?? "", r.Email, r.Phone, r.City ?? "", r.Status.ToString(), r.Owner, Fmt.Date(clock.ToLocal(r.CreatedDate)) }).ToList(),
            Page = f.Page, PageSize = f.PageSize, TotalCount = total, Scope = scope.Description,
            StatusOptions = Options<CustomerStatus>(),
            Summary = [Kv("Customers", total), .. Enum.GetValues<CustomerStatus>().Select(s => Kv(s.ToString(), statusCounts.FirstOrDefault(x => x.Key == s)?.Count ?? 0))]
        };
    }

    public async Task<ReportTable> LeadsAsync(ReportFilter f, CancellationToken ct = default)
    {
        f.Normalize(int.MaxValue);
        var q = await scope.ApplyAsync(db.Leads.AsNoTracking(), ct);
        q = ApplyCreatedRange(q, f, l => l.CreatedDate);
        if (Enum.TryParse<LeadStatus>(f.Status, out var st)) q = q.Where(l => l.Status == st);
        if (!string.IsNullOrEmpty(f.OwnerId)) q = q.Where(l => l.AssignedToId == f.OwnerId);
        if (f.Search is { } s)
        {
            var like = $"%{s}%";
            q = q.Where(l => EF.Functions.Like(l.LeadName, like) || (l.CompanyName != null && EF.Functions.Like(l.CompanyName, like)));
        }

        var counts = await q.GroupBy(l => l.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var d = f.Descending;
        q = f.Sort switch
        {
            "code" => d ? q.OrderByDescending(l => l.LeadCode) : q.OrderBy(l => l.LeadCode),
            "name" => d ? q.OrderByDescending(l => l.LeadName) : q.OrderBy(l => l.LeadName),
            "source" => d ? q.OrderByDescending(l => l.Source) : q.OrderBy(l => l.Source),
            "status" => d ? q.OrderByDescending(l => l.Status) : q.OrderBy(l => l.Status),
            "value" => d ? q.OrderByDescending(l => l.ExpectedValue) : q.OrderBy(l => l.ExpectedValue),
            "owner" => d ? q.OrderByDescending(l => l.AssignedTo!.FullName) : q.OrderBy(l => l.AssignedTo!.FullName),
            "created" when !d => q.OrderBy(l => l.CreatedDate),
            _ => q.OrderByDescending(l => l.CreatedDate)
        };
        var total = await q.CountAsync(ct);
        var rows = await Page(q, f).Select(l => new
        {
            l.LeadCode, l.LeadName, l.CompanyName, l.Source, l.Status, l.Priority, l.ExpectedValue, Owner = l.AssignedTo!.FullName,
            l.CreatedDate, l.ConvertedDate, Customer = l.ConvertedCustomer != null ? l.ConvertedCustomer.CustomerCode : null
        }).ToListAsync(ct);

        var converted = counts.FirstOrDefault(x => x.Key == LeadStatus.Converted)?.Count ?? 0;
        return new ReportTable
        {
            Key = "leads", Title = "Lead Report", Description = "Lead source, status, owner and conversion information.",
            Columns = [new("code", "Code"), new("name", "Lead"), new("company", "Company", false), new("source", "Source"), new("status", "Status"),
                new("priority", "Priority", false), new("value", "Expected Value", true, true), new("owner", "Owner"), new("created", "Created"),
                new("converted", "Converted On", false), new("customer", "Customer", false)],
            Rows = rows.Select(r => new[]
            {
                r.LeadCode, r.LeadName, r.CompanyName ?? "", Fmt.Humanize(r.Source), r.Status.ToString(), r.Priority.ToString(),
                Fmt.Money(r.ExpectedValue), r.Owner, Fmt.Date(clock.ToLocal(r.CreatedDate)),
                r.ConvertedDate is { } cd ? Fmt.Date(clock.ToLocal(cd)) : "", r.Customer ?? ""
            }).ToList(),
            Page = f.Page, PageSize = f.PageSize, TotalCount = total, Scope = scope.Description,
            StatusOptions = Options<LeadStatus>(),
            Summary =
            [
                Kv("Leads", total), Kv("Open", counts.Where(x => LeadWorkflow.OpenStatuses.Contains(x.Key)).Sum(x => x.Count)),
                Kv("Converted", converted), Kv("Lost", counts.FirstOrDefault(x => x.Key == LeadStatus.Lost)?.Count ?? 0),
                new("Conversion Rate", Fmt.Percent(total == 0 ? 0 : converted * 100.0 / total))
            ]
        };
    }

    public async Task<ReportTable> FollowUpsAsync(ReportFilter f, CancellationToken ct = default)
    {
        f.Normalize(int.MaxValue);
        var q = await scope.ApplyAsync(db.FollowUps.AsNoTracking(), ct);
        if (f.From is { } from) { var fd = from.Date; q = q.Where(x => x.FollowUpDate >= fd); }
        if (f.To is { } to) { var td = to.Date.AddDays(1); q = q.Where(x => x.FollowUpDate < td); }
        if (!string.IsNullOrEmpty(f.OwnerId)) q = q.Where(x => x.AssignedToId == f.OwnerId);
        var now = clock.Now;
        if (f.Status == "Overdue") q = q.Where(x => x.Status == FollowUpStatus.Planned && x.FollowUpDate < now);
        else if (Enum.TryParse<FollowUpStatus>(f.Status, out var st)) q = q.Where(x => x.Status == st);
        if (f.Search is { } s) { var like = $"%{s}%"; q = q.Where(x => EF.Functions.Like(x.Subject, like)); }

        var counts = await q.GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var overdue = await q.CountAsync(x => x.Status == FollowUpStatus.Planned && x.FollowUpDate < now, ct);
        var d = f.Descending;
        q = f.Sort switch
        {
            "subject" => d ? q.OrderByDescending(x => x.Subject) : q.OrderBy(x => x.Subject),
            "status" => d ? q.OrderByDescending(x => x.Status) : q.OrderBy(x => x.Status),
            "owner" => d ? q.OrderByDescending(x => x.AssignedTo!.FullName) : q.OrderBy(x => x.AssignedTo!.FullName),
            "date" when d => q.OrderByDescending(x => x.FollowUpDate),
            _ => q.OrderBy(x => x.FollowUpDate)
        };
        var total = await q.CountAsync(ct);
        var rows = await Page(q, f).Select(x => new
        {
            x.FollowUpDate, x.Subject, x.FollowUpType, x.Status, Owner = x.AssignedTo!.FullName, x.CompletedDate,
            Related = x.Customer != null ? "Customer: " + x.Customer.CustomerName : x.Lead != null ? "Lead: " + x.Lead.LeadName
                : x.Opportunity != null ? "Opportunity: " + x.Opportunity.OpportunityName : ""
        }).ToListAsync(ct);

        return new ReportTable
        {
            Key = "followups", Title = "Follow-Up Report", Description = "Planned, completed, missed and overdue follow-up activities.",
            Columns = [new("date", "Follow-Up Date"), new("subject", "Subject"), new("type", "Type", false), new("status", "Status"),
                new("related", "Related To", false), new("owner", "Assigned To"), new("completed", "Completed On", false)],
            Rows = rows.Select(r => new[]
            {
                Fmt.DateTime(r.FollowUpDate), r.Subject, r.FollowUpType.ToString(),
                FollowUpRules.IsOverdue(r.Status, r.FollowUpDate, now) ? "Overdue" : r.Status.ToString(),
                r.Related, r.Owner, r.CompletedDate is { } c ? Fmt.DateTime(clock.ToLocal(c)) : ""
            }).ToList(),
            Page = f.Page, PageSize = f.PageSize, TotalCount = total, Scope = scope.Description, DateLabel = "Follow-up date",
            StatusOptions = [.. Options<FollowUpStatus>(), new OptionDto("Overdue", "Overdue")],
            Summary =
            [
                Kv("Planned", counts.FirstOrDefault(x => x.Key == FollowUpStatus.Planned)?.Count ?? 0), Kv("Overdue", overdue),
                Kv("Completed", counts.FirstOrDefault(x => x.Key == FollowUpStatus.Completed)?.Count ?? 0),
                Kv("Missed", counts.FirstOrDefault(x => x.Key == FollowUpStatus.Missed)?.Count ?? 0),
                Kv("Cancelled", counts.FirstOrDefault(x => x.Key == FollowUpStatus.Cancelled)?.Count ?? 0)
            ]
        };
    }

    public async Task<ReportTable> OpportunitiesAsync(ReportFilter f, CancellationToken ct = default)
    {
        f.Normalize(int.MaxValue);
        var q = await scope.ApplyAsync(db.Opportunities.AsNoTracking(), ct);
        if (f.From is { } from) { var fd = from.Date; q = q.Where(o => o.ExpectedCloseDate >= fd); }
        if (f.To is { } to) { var td = to.Date.AddDays(1); q = q.Where(o => o.ExpectedCloseDate < td); }
        if (Enum.TryParse<OpportunityStage>(f.Status, out var st)) q = q.Where(o => o.Stage == st);
        if (!string.IsNullOrEmpty(f.OwnerId)) q = q.Where(o => o.AssignedToId == f.OwnerId);
        if (f.Search is { } s)
        {
            var like = $"%{s}%";
            q = q.Where(o => EF.Functions.Like(o.OpportunityName, like) || EF.Functions.Like(o.Customer!.CustomerName, like));
        }

        var totals = await q.Select(o => new { o.Amount, o.Probability }).ToListAsync(ct);
        var d = f.Descending;
        q = f.Sort switch
        {
            "name" => d ? q.OrderByDescending(o => o.OpportunityName) : q.OrderBy(o => o.OpportunityName),
            "customer" => d ? q.OrderByDescending(o => o.Customer!.CustomerName) : q.OrderBy(o => o.Customer!.CustomerName),
            "stage" => d ? q.OrderByDescending(o => o.Stage) : q.OrderBy(o => o.Stage),
            "amount" => d ? q.OrderByDescending(o => o.Amount) : q.OrderBy(o => o.Amount),
            "probability" => d ? q.OrderByDescending(o => o.Probability) : q.OrderBy(o => o.Probability),
            "owner" => d ? q.OrderByDescending(o => o.AssignedTo!.FullName) : q.OrderBy(o => o.AssignedTo!.FullName),
            "close" when d => q.OrderByDescending(o => o.ExpectedCloseDate),
            _ => q.OrderBy(o => o.ExpectedCloseDate)
        };
        var total = await q.CountAsync(ct);
        var rows = await Page(q, f).Select(o => new { o.OpportunityName, Customer = o.Customer!.CustomerName, o.Stage, o.Amount, o.Probability, o.ExpectedCloseDate, Owner = o.AssignedTo!.FullName }).ToListAsync(ct);

        return new ReportTable
        {
            Key = "opportunities", Title = "Opportunity Report", Description = "Stage, amount, probability and expected close date.",
            Columns = [new("name", "Opportunity"), new("customer", "Customer"), new("stage", "Stage"), new("amount", "Amount", true, true),
                new("probability", "Probability", true, true), new("weighted", "Weighted", false, true), new("close", "Expected Close"), new("owner", "Owner")],
            Rows = rows.Select(r => new[]
            {
                r.OpportunityName, r.Customer, r.Stage.ToString(), Fmt.Money(r.Amount), r.Probability + "%",
                Fmt.Money(OpportunityRules.Weighted(r.Amount, r.Probability)), Fmt.Date(r.ExpectedCloseDate), r.Owner
            }).ToList(),
            Page = f.Page, PageSize = f.PageSize, TotalCount = total, Scope = scope.Description, DateLabel = "Expected close",
            StatusOptions = Options<OpportunityStage>(), StatusLabel = "Stage",
            Summary =
            [
                Kv("Opportunities", total), new("Total Amount", Fmt.Money(totals.Sum(t => t.Amount))),
                new("Weighted Amount", Fmt.Money(totals.Sum(t => OpportunityRules.Weighted(t.Amount, t.Probability))))
            ]
        };
    }

    public async Task<ReportTable> UserActivityAsync(ReportFilter f, CancellationToken ct = default)
    {
        f.Normalize(int.MaxValue);
        f.From ??= clock.Today.AddDays(-29);
        var fromUtc = clock.ToUtc(f.From.Value.Date);
        var toUtc = f.To is { } t ? clock.ToUtc(t.Date.AddDays(1)) : clock.UtcNow.AddMinutes(1);

        var visible = scope.IsAdmin ? null : await scope.VisibleOwnerIdsAsync(ct) ?? [];
        var q = audit.ScopedQuery(visible, limited: false).Where(a => a.UserId != null && a.CreatedDate >= fromUtc && a.CreatedDate < toUtc);
        if (!string.IsNullOrEmpty(f.OwnerId)) q = q.Where(a => a.UserId == f.OwnerId);

        var grouped = await q.GroupBy(a => a.UserId!).Select(g => new
        {
            UserId = g.Key,
            Total = g.Count(),
            Logins = g.Count(a => a.Action == AuditAction.Login),
            Failed = g.Count(a => a.Action == AuditAction.LoginFailed),
            Creates = g.Count(a => a.Action == AuditAction.Create),
            Updates = g.Count(a => a.Action == AuditAction.Update || a.Action == AuditAction.StatusChange || a.Action == AuditAction.Assign
                                   || a.Action == AuditAction.Complete || a.Action == AuditAction.Reschedule || a.Action == AuditAction.Convert),
            Deletes = g.Count(a => a.Action == AuditAction.Delete),
            Last = g.Max(a => a.CreatedDate)
        }).ToListAsync(ct);

        var ids = grouped.Select(g => g.UserId).ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ids.Contains(ur.UserId) select new { ur.UserId, r.Name })
            .ToDictionaryAsync(x => x.UserId, x => x.Name, ct);

        var list = grouped.Select(g => new
        {
            Name = users.GetValueOrDefault(g.UserId, "(deleted user)"), Role = AppRoles.DisplayName(roles.GetValueOrDefault(g.UserId)), g.Total,
            g.Logins, g.Failed, g.Creates, g.Updates, g.Deletes, g.Last
        });
        if (f.Search is { } s) list = list.Where(x => x.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
        list = f.Sort switch
        {
            "user" => f.Descending ? list.OrderByDescending(x => x.Name) : list.OrderBy(x => x.Name),
            "logins" => f.Descending ? list.OrderByDescending(x => x.Logins) : list.OrderBy(x => x.Logins),
            "last" => f.Descending ? list.OrderByDescending(x => x.Last) : list.OrderBy(x => x.Last),
            "total" when !f.Descending => list.OrderBy(x => x.Total),
            _ => list.OrderByDescending(x => x.Total)
        };
        var all = list.ToList();
        var page = all.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize);

        return new ReportTable
        {
            Key = "user-activity", Title = "User Activity Report", Description = "Actions and activity counts per user (from the audit trail).",
            Columns = [new("user", "User"), new("role", "Role", false), new("total", "Total Actions", true, true), new("logins", "Logins", true, true),
                new("failed", "Failed Logins", false, true), new("creates", "Creates", false, true), new("updates", "Updates", false, true),
                new("deletes", "Deletes", false, true), new("last", "Last Activity")],
            Rows = page.Select(x => new[]
            {
                x.Name, x.Role, Fmt.Number(x.Total), Fmt.Number(x.Logins), Fmt.Number(x.Failed), Fmt.Number(x.Creates),
                Fmt.Number(x.Updates), Fmt.Number(x.Deletes), Fmt.DateTime(clock.ToLocal(x.Last))
            }).ToList(),
            Page = f.Page, PageSize = f.PageSize, TotalCount = all.Count, Scope = scope.IsAdmin ? "All users" : "Team",
            DateLabel = "Activity date", StatusOptions = [],
            Summary = [Kv("Active Users", all.Count), Kv("Total Actions", all.Sum(x => x.Total)), Kv("Logins", all.Sum(x => x.Logins)), Kv("Failed Logins", all.Sum(x => x.Failed))]
        };
    }

    public async Task<ReportTable> AuditAsync(ReportFilter f, CancellationToken ct = default)
    {
        f.Normalize(int.MaxValue);
        var q = audit.ScopedQuery(null, limited: false);
        if (f.From is { } from) { var fu = clock.ToUtc(from.Date); q = q.Where(a => a.CreatedDate >= fu); }
        if (f.To is { } to) { var tu = clock.ToUtc(to.Date.AddDays(1)); q = q.Where(a => a.CreatedDate < tu); }
        if (Enum.TryParse<AuditModule>(f.Status, out var module)) q = q.Where(a => a.Module == module);
        if (!string.IsNullOrEmpty(f.OwnerId)) q = q.Where(a => a.UserId == f.OwnerId);
        if (f.Search is { } s) q = q.Where(a => (a.Details != null && a.Details.Contains(s)) || (a.UserName != null && a.UserName.Contains(s)));

        var failures = await q.CountAsync(a => a.Result == AuditResult.Failure, ct);
        q = f.Sort switch
        {
            "user" => f.Descending ? q.OrderByDescending(a => a.UserName) : q.OrderBy(a => a.UserName),
            "action" => f.Descending ? q.OrderByDescending(a => a.Action) : q.OrderBy(a => a.Action),
            "time" when !f.Descending => q.OrderBy(a => a.AuditLogId),
            _ => q.OrderByDescending(a => a.AuditLogId)
        };
        var total = await q.CountAsync(ct);
        var rows = await Page(q, f).ToListAsync(ct);
        return new ReportTable
        {
            Key = "audit", Title = "Audit Report", Description = "Security and business audit events.",
            Columns = [new("time", "Time"), new("user", "User"), new("module", "Module", false), new("action", "Action"), new("entity", "Entity", false),
                new("record", "Record", false), new("result", "Result", false), new("ip", "IP Address", false), new("details", "Details", false)],
            Rows = rows.Select(a => new[]
            {
                Fmt.DateTime(clock.ToLocal(a.CreatedDate)), a.UserName ?? "—", a.Module.ToString(), Fmt.Humanize(a.Action), a.EntityName ?? "",
                a.RecordId ?? "", a.Result.ToString(), a.IpAddress ?? "", a.Details ?? ""
            }).ToList(),
            Page = f.Page, PageSize = f.PageSize, TotalCount = total, Scope = "All events", DateLabel = "Event date",
            StatusOptions = Options<AuditModule>(), StatusLabel = "Module",
            Summary = [Kv("Events", total), Kv("Failures", failures)]
        };
    }

    public async Task<ConversionReportDto> ConversionAsync(ReportFilter f, CancellationToken ct = default)
    {
        f.Normalize(int.MaxValue);
        var leadQ = await scope.ApplyAsync(db.Leads.AsNoTracking(), ct);
        leadQ = ApplyCreatedRange(leadQ, f, l => l.CreatedDate);
        if (!string.IsNullOrEmpty(f.OwnerId)) leadQ = leadQ.Where(l => l.AssignedToId == f.OwnerId);
        var leads = await leadQ.Select(l => new { l.Status, l.Source, Owner = l.AssignedTo!.FullName }).ToListAsync(ct);

        var oppQ = await scope.ApplyAsync(db.Opportunities.AsNoTracking(), ct);
        oppQ = oppQ.Where(o => o.ClosedDate != null);
        if (f.From is { } from) { var fu = clock.ToUtc(from.Date); oppQ = oppQ.Where(o => o.ClosedDate >= fu); }
        if (f.To is { } to) { var tu = clock.ToUtc(to.Date.AddDays(1)); oppQ = oppQ.Where(o => o.ClosedDate < tu); }
        if (!string.IsNullOrEmpty(f.OwnerId)) oppQ = oppQ.Where(o => o.AssignedToId == f.OwnerId);
        var closed = await oppQ.Select(o => new { o.Stage, o.Amount }).ToListAsync(ct);

        static double Rate(int part, int whole) => whole == 0 ? 0 : Math.Round(part * 100.0 / whole, 1);
        var converted = leads.Count(l => l.Status == LeadStatus.Converted);
        var won = closed.Where(o => o.Stage == OpportunityStage.Won).ToList();
        var lostOpps = closed.Count(o => o.Stage == OpportunityStage.Lost);

        return new ConversionReportDto
        {
            TotalLeads = leads.Count,
            ConvertedLeads = converted,
            LostLeads = leads.Count(l => l.Status == LeadStatus.Lost),
            UnqualifiedLeads = leads.Count(l => l.Status == LeadStatus.Unqualified),
            OpenLeads = leads.Count(l => LeadWorkflow.OpenStatuses.Contains(l.Status)),
            ConversionRate = Rate(converted, leads.Count),
            WonOpportunities = won.Count,
            LostOpportunities = lostOpps,
            WinRate = Rate(won.Count, won.Count + lostOpps),
            WonAmount = won.Sum(o => o.Amount),
            AverageDealSize = won.Count == 0 ? 0 : Math.Round(won.Average(o => o.Amount), 2),
            BySource = leads.GroupBy(l => l.Source).Select(g => new ConversionBreakdown(Fmt.Humanize(g.Key), g.Count(),
                g.Count(l => l.Status == LeadStatus.Converted), g.Count(l => l.Status == LeadStatus.Lost), Rate(g.Count(l => l.Status == LeadStatus.Converted), g.Count())))
                .OrderByDescending(b => b.Total).ToList(),
            ByOwner = leads.GroupBy(l => l.Owner).Select(g => new ConversionBreakdown(g.Key, g.Count(),
                g.Count(l => l.Status == LeadStatus.Converted), g.Count(l => l.Status == LeadStatus.Lost), Rate(g.Count(l => l.Status == LeadStatus.Converted), g.Count())))
                .OrderByDescending(b => b.ConversionRate).ToList(),
            Scope = scope.Description
        };
    }

    /// <summary>CSV export with spreadsheet formula-injection protection.</summary>
    public static byte[] ToCsv(ReportTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", table.Columns.Select(c => Escape(c.Header))));
        foreach (var row in table.Rows) sb.AppendLine(string.Join(",", row.Select(Escape)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    internal static string Escape(string? value)
    {
        var v = value ?? "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0]) && !decimal.TryParse(v, out _)) v = "'" + v;
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private IQueryable<T> ApplyCreatedRange<T>(IQueryable<T> q, ReportFilter f, System.Linq.Expressions.Expression<Func<T, DateTime>> created)
    {
        if (f.From is { } from)
        {
            var fu = clock.ToUtc(from.Date);
            q = q.Where(Compose(created, d => d >= fu));
        }
        if (f.To is { } to)
        {
            var tu = clock.ToUtc(to.Date.AddDays(1));
            q = q.Where(Compose(created, d => d < tu));
        }
        return q;
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> Compose<T>(
        System.Linq.Expressions.Expression<Func<T, DateTime>> selector, System.Linq.Expressions.Expression<Func<DateTime, bool>> predicate)
    {
        var body = new ReplaceParameter(predicate.Parameters[0], selector.Body).Visit(predicate.Body)!;
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, selector.Parameters);
    }

    private sealed class ReplaceParameter(System.Linq.Expressions.ParameterExpression from, System.Linq.Expressions.Expression to)
        : System.Linq.Expressions.ExpressionVisitor
    {
        protected override System.Linq.Expressions.Expression VisitParameter(System.Linq.Expressions.ParameterExpression node) => node == from ? to : node;
    }

    private static IQueryable<T> Page<T>(IQueryable<T> q, ReportFilter f) =>
        f.PageSize >= int.MaxValue / 2 ? q : q.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize);

    private static KeyValuePair<string, string> Kv(string label, int value) => new(label, Fmt.Number(value));

    private static List<OptionDto> Options<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(e => new OptionDto(e.ToString(), Fmt.Humanize(e))).ToList();
}
