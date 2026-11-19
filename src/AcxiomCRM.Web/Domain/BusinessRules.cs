using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Validation;

namespace AcxiomCRM.Web.Domain;

/// <summary>Field-keyed business-rule violations.</summary>
public sealed class RuleViolations
{
    public Dictionary<string, List<string>> Errors { get; } = new();
    public bool Any => Errors.Count > 0;

    public RuleViolations Add(string field, string message)
    {
        if (!Errors.TryGetValue(field, out var list)) Errors[field] = list = [];
        if (!list.Contains(message)) list.Add(message);
        return this;
    }
}

/// <summary>Lead status workflow (New → Contacted → Qualified → Converted, with Unqualified/Lost exits).</summary>
public static class LeadWorkflow
{
    private static readonly Dictionary<LeadStatus, LeadStatus[]> Transitions = new()
    {
        [LeadStatus.New] = [LeadStatus.Contacted, LeadStatus.Unqualified, LeadStatus.Lost],
        [LeadStatus.Contacted] = [LeadStatus.Qualified, LeadStatus.Unqualified, LeadStatus.Lost],
        [LeadStatus.Qualified] = [LeadStatus.Contacted, LeadStatus.Unqualified, LeadStatus.Lost],
        [LeadStatus.Unqualified] = [LeadStatus.Contacted, LeadStatus.Lost],
        [LeadStatus.Converted] = [],
        [LeadStatus.Lost] = []
    };

    /// <summary>Statuses a brand-new lead may start in.</summary>
    public static readonly LeadStatus[] InitialStatuses = [LeadStatus.New, LeadStatus.Contacted, LeadStatus.Qualified];

    /// <summary>Statuses counted as "open" on the dashboard.</summary>
    public static readonly LeadStatus[] OpenStatuses = [LeadStatus.New, LeadStatus.Contacted, LeadStatus.Qualified];

    public static IReadOnlyList<LeadStatus> NextStatuses(LeadStatus current) => Transitions[current];

    /// <summary>Converted can only be reached through the conversion workflow, never by a plain status change.</summary>
    public static bool CanTransition(LeadStatus from, LeadStatus to) => from == to || Transitions[from].Contains(to);

    public static bool CanConvert(LeadStatus status) => status == LeadStatus.Qualified;

    public static bool IsClosed(LeadStatus status) => status is LeadStatus.Converted or LeadStatus.Lost;
}

public static class OpportunityRules
{
    public static readonly OpportunityStage[] ActiveStages = [OpportunityStage.Qualification, OpportunityStage.Proposal, OpportunityStage.Negotiation];

    public static bool IsActive(OpportunityStage stage) => ActiveStages.Contains(stage);

    public static OpportunityStatus StatusFor(OpportunityStage stage) => stage switch
    {
        OpportunityStage.Won => OpportunityStatus.Won,
        OpportunityStage.Lost => OpportunityStatus.Lost,
        _ => OpportunityStatus.Open
    };

    /// <summary>Weighted pipeline = Amount × Probability / 100.</summary>
    public static decimal Weighted(decimal amount, int probability) => Math.Round(amount * probability / 100m, 2);

    /// <summary>Default probability per stage, used when a stage changes on the pipeline board.</summary>
    public static int DefaultProbability(OpportunityStage stage) => stage switch
    {
        OpportunityStage.Qualification => 20,
        OpportunityStage.Proposal => 50,
        OpportunityStage.Negotiation => 75,
        OpportunityStage.Won => 100,
        _ => 0
    };

    public static RuleViolations Validate(OpportunityStage stage, decimal amount, int probability, DateTime expectedCloseDate,
        DateTime today, OpportunityStage? currentStage, string? closeReason)
    {
        var v = new RuleViolations();
        var active = IsActive(stage);

        if (amount < 0) v.Add("Amount", ValidationMessages.AmountNotNegative);
        else if (active && amount <= 0) v.Add("Amount", ValidationMessages.AmountPositive);

        if (probability is < 0 or > 100) v.Add("Probability", ValidationMessages.Probability);

        if (active && expectedCloseDate.Date < today) v.Add("ExpectedCloseDate", ValidationMessages.CloseDatePast);

        if (currentStage is { } from && !IsActive(from) && from != stage)
            v.Add("Stage", $"A {from} opportunity is closed and cannot be moved to {stage}.");

        if (stage == OpportunityStage.Lost && string.IsNullOrWhiteSpace(closeReason))
            v.Add("CloseReason", "Record the reason the opportunity was lost.");

        return v;
    }
}

public static class FollowUpRules
{
    public static bool IsOverdue(FollowUpStatus status, DateTime followUpDate, DateTime now) =>
        status == FollowUpStatus.Planned && followUpDate < now;

    public static bool CanComplete(FollowUpStatus s) => s == FollowUpStatus.Planned;
    public static bool CanMarkMissed(FollowUpStatus s) => s == FollowUpStatus.Planned;
    public static bool CanReschedule(FollowUpStatus s) => s is FollowUpStatus.Planned or FollowUpStatus.Missed;
    public static bool CanCancel(FollowUpStatus s) => s is FollowUpStatus.Planned or FollowUpStatus.Missed;

    /// <summary>New or rescheduled (planned) follow-ups may not be dated before today.</summary>
    public static RuleViolations ValidateDate(DateTime date, DateTime today, string field = "FollowUpDate")
    {
        var v = new RuleViolations();
        if (date.Date < today) v.Add(field, ValidationMessages.FollowUpDatePast);
        return v;
    }

    public static ActivityType ActivityTypeFor(FollowUpType t) => t switch
    {
        FollowUpType.Call => ActivityType.Call,
        FollowUpType.Meeting or FollowUpType.Visit => ActivityType.Meeting,
        FollowUpType.Email => ActivityType.Email,
        _ => ActivityType.Task
    };
}
