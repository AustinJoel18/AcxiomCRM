namespace AcxiomCRM.Web.Models.Entities;

public class Customer : IOwnedEntity, ITrackedEntity, ISoftDelete, IAuditableEntity
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public CustomerStatus Status { get; set; } = CustomerStatus.Active;
    public string? Notes { get; set; }

    /// <summary>Lead this customer was converted from, if any.</summary>
    public int? SourceLeadId { get; set; }

    public string AssignedToId { get; set; } = string.Empty;
    public ApplicationUser? AssignedTo { get; set; }

    public DateTime CreatedDate { get; set; }
    public string? CreatedById { get; set; }
    public ApplicationUser? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string? DeletedById { get; set; }

    public ICollection<Opportunity> Opportunities { get; set; } = new List<Opportunity>();
    public ICollection<FollowUp> FollowUps { get; set; } = new List<FollowUp>();
    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
}

public class Lead : IOwnedEntity, ITrackedEntity, ISoftDelete, IAuditableEntity
{
    public int LeadId { get; set; }
    public string LeadCode { get; set; } = string.Empty;
    public string LeadName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? CompanyName { get; set; }
    public LeadSource Source { get; set; }
    public LeadStatus Status { get; set; } = LeadStatus.New;
    public LeadPriority Priority { get; set; } = LeadPriority.Medium;
    public decimal? ExpectedValue { get; set; }
    public string? Notes { get; set; }

    public DateTime? ConvertedDate { get; set; }
    public int? ConvertedCustomerId { get; set; }
    public Customer? ConvertedCustomer { get; set; }
    public int? ConvertedOpportunityId { get; set; }

    public string AssignedToId { get; set; } = string.Empty;
    public ApplicationUser? AssignedTo { get; set; }

    public DateTime CreatedDate { get; set; }
    public string? CreatedById { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string? DeletedById { get; set; }

    public ICollection<FollowUp> FollowUps { get; set; } = new List<FollowUp>();
    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
}

public class Opportunity : IOwnedEntity, ITrackedEntity, ISoftDelete, IAuditableEntity
{
    public int OpportunityId { get; set; }
    public string OpportunityName { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }

    public decimal Amount { get; set; }
    public OpportunityStage Stage { get; set; } = OpportunityStage.Qualification;
    public int Probability { get; set; }
    public DateTime ExpectedCloseDate { get; set; }

    /// <summary>Derived from Stage: Open while in an active stage, otherwise Won / Lost.</summary>
    public OpportunityStatus Status { get; set; } = OpportunityStatus.Open;

    public LeadSource? Source { get; set; }
    public string? Notes { get; set; }

    /// <summary>Final outcome notes captured when the opportunity is marked Won or Lost.</summary>
    public string? CloseReason { get; set; }
    public DateTime? ClosedDate { get; set; }

    public string AssignedToId { get; set; } = string.Empty;
    public ApplicationUser? AssignedTo { get; set; }

    public DateTime CreatedDate { get; set; }
    public string? CreatedById { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string? DeletedById { get; set; }

    public ICollection<FollowUp> FollowUps { get; set; } = new List<FollowUp>();
}

public class FollowUp : IOwnedEntity, ITrackedEntity, ISoftDelete, IAuditableEntity
{
    public int FollowUpId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public FollowUpType FollowUpType { get; set; }

    /// <summary>Scheduled date/time in the business time zone.</summary>
    public DateTime FollowUpDate { get; set; }

    public FollowUpStatus Status { get; set; } = FollowUpStatus.Planned;
    public string? Remarks { get; set; }
    public string? Outcome { get; set; }
    public DateTime? CompletedDate { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }
    public int? OpportunityId { get; set; }
    public Opportunity? Opportunity { get; set; }

    public string AssignedToId { get; set; } = string.Empty;
    public ApplicationUser? AssignedTo { get; set; }

    public DateTime CreatedDate { get; set; }
    public string? CreatedById { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string? DeletedById { get; set; }

    public RelatedRecordType? RelatedType =>
        CustomerId.HasValue ? RelatedRecordType.Customer
        : LeadId.HasValue ? RelatedRecordType.Lead
        : OpportunityId.HasValue ? RelatedRecordType.Opportunity
        : null;

    public int? RelatedId => CustomerId ?? LeadId ?? OpportunityId;
}

public class Activity : IOwnedEntity, ITrackedEntity, ISoftDelete, IAuditableEntity
{
    public int ActivityId { get; set; }
    public ActivityType ActivityType { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Date/time of the activity in the business time zone.</summary>
    public DateTime ActivityDate { get; set; }

    public ActivityStatus Status { get; set; } = ActivityStatus.Planned;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }
    public int? OpportunityId { get; set; }
    public Opportunity? Opportunity { get; set; }
    public int? FollowUpId { get; set; }

    public string AssignedToId { get; set; } = string.Empty;
    public ApplicationUser? AssignedTo { get; set; }

    public DateTime CreatedDate { get; set; }
    public string? CreatedById { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string? DeletedById { get; set; }
}

/// <summary>Append-only security and business audit trail.</summary>
public class AuditLog
{
    public long AuditLogId { get; set; }

    /// <summary>UTC timestamp.</summary>
    public DateTime CreatedDate { get; set; }

    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public AuditModule Module { get; set; }
    public AuditAction Action { get; set; }
    public string? EntityName { get; set; }
    public string? RecordId { get; set; }
    public AuditResult Result { get; set; } = AuditResult.Success;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
