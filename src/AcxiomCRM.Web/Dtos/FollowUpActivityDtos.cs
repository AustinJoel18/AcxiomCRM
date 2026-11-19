using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Validation;

namespace AcxiomCRM.Web.Dtos;

public sealed class FollowUpQuery : ListQuery
{
    /// <summary>"pending" (planned, not overdue), "overdue", "today", "upcoming" or empty for all.</summary>
    public string? View { get; set; }
    public FollowUpStatus? Status { get; set; }
    public string? AssignedToId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public RelatedRecordType? RelatedType { get; set; }
    public int? RelatedId { get; set; }
}

public sealed class FollowUpDto
{
    public int FollowUpId { get; init; }
    public string Subject { get; init; } = "";
    public FollowUpType FollowUpType { get; init; }
    public DateTime FollowUpDate { get; init; }
    public FollowUpStatus Status { get; init; }
    public bool IsOverdue { get; init; }
    public string? Remarks { get; init; }
    public string? Outcome { get; init; }
    public DateTime? CompletedDate { get; init; }
    public RelatedRecordType? RelatedType { get; init; }
    public int? RelatedId { get; init; }
    public string? RelatedName { get; init; }
    public int? CustomerId { get; init; }
    public int? LeadId { get; init; }
    public int? OpportunityId { get; init; }
    public string AssignedToId { get; init; } = "";
    public string? AssignedToName { get; init; }
    public DateTime CreatedDate { get; init; }
}

/// <summary>Fields shared by create and edit.</summary>
public class FollowUpEditRequest
{
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(200, MinimumLength = 3, ErrorMessage = ValidationMessages.LengthRange)]
    public string? Subject { get; set; }

    [Display(Name = "Type")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [ValidEnum]
    public FollowUpType? FollowUpType { get; set; } = Models.FollowUpType.Call;

    [StringLength(1000, ErrorMessage = ValidationMessages.MaxLength)]
    public string? Remarks { get; set; }

    /// <summary>Customer, Lead or Opportunity.</summary>
    [Display(Name = "Related To")]
    [ValidEnum]
    public RelatedRecordType? RelatedType { get; set; }

    [Display(Name = "Related Record")]
    public int? RelatedId { get; set; }

    [Display(Name = "Assigned To")]
    [StringLength(450)]
    public string? AssignedToId { get; set; }
}

public class FollowUpCreateRequest : FollowUpEditRequest
{
    [Display(Name = "Follow-Up Date")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [DataType(DataType.DateTime)]
    [NotBeforeToday(ValidationMessages.FollowUpDatePast)]
    public DateTime? FollowUpDate { get; set; }
}

public class FollowUpCompleteRequest
{
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(1000, MinimumLength = 3, ErrorMessage = ValidationMessages.LengthRange)]
    public string? Outcome { get; set; }
}

public class FollowUpRescheduleRequest
{
    [Display(Name = "New Follow-Up Date")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [DataType(DataType.DateTime)]
    [NotBeforeToday(ValidationMessages.FollowUpDatePast)]
    public DateTime? NewDate { get; set; }

    [StringLength(500, ErrorMessage = ValidationMessages.MaxLength)]
    public string? Reason { get; set; }
}

public sealed class ActivityQuery : ListQuery
{
    public ActivityType? Type { get; set; }
    public ActivityStatus? Status { get; set; }
    public string? AssignedToId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

public sealed class ActivityDto
{
    public int ActivityId { get; init; }
    public ActivityType ActivityType { get; init; }
    public string Subject { get; init; } = "";
    public string? Description { get; init; }
    public DateTime ActivityDate { get; init; }
    public ActivityStatus Status { get; init; }
    public RelatedRecordType? RelatedType { get; init; }
    public int? RelatedId { get; init; }
    public string? RelatedName { get; init; }
    public int? FollowUpId { get; init; }
    public string AssignedToId { get; init; } = "";
    public string? AssignedToName { get; init; }
    public DateTime CreatedDate { get; init; }
}

public class ActivitySaveRequest
{
    [Display(Name = "Activity Type")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [ValidEnum]
    public ActivityType? ActivityType { get; set; } = Models.ActivityType.Call;

    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(200, MinimumLength = 3, ErrorMessage = ValidationMessages.LengthRange)]
    public string? Subject { get; set; }

    [StringLength(2000, ErrorMessage = ValidationMessages.MaxLength)]
    public string? Description { get; set; }

    [Display(Name = "Activity Date")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [DataType(DataType.DateTime)]
    public DateTime? ActivityDate { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [ValidEnum]
    public ActivityStatus? Status { get; set; } = ActivityStatus.Planned;

    [Display(Name = "Related To")]
    [ValidEnum]
    public RelatedRecordType? RelatedType { get; set; }

    [Display(Name = "Related Record")]
    public int? RelatedId { get; set; }

    [Display(Name = "Assigned To")]
    [StringLength(450)]
    public string? AssignedToId { get; set; }
}
