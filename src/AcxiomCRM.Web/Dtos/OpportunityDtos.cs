using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Validation;

namespace AcxiomCRM.Web.Dtos;

public sealed class OpportunityQuery : ListQuery
{
    public OpportunityStage? Stage { get; set; }
    public OpportunityStatus? Status { get; set; }
    public int? CustomerId { get; set; }
    public string? AssignedToId { get; set; }
}

public sealed class OpportunityDto
{
    public int OpportunityId { get; init; }
    public string OpportunityName { get; init; } = "";
    public int CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public int? LeadId { get; init; }
    public string? LeadName { get; init; }
    public decimal Amount { get; init; }
    public OpportunityStage Stage { get; init; }
    public OpportunityStatus Status { get; init; }
    public int Probability { get; init; }
    public decimal WeightedAmount { get; init; }
    public DateTime ExpectedCloseDate { get; init; }
    public LeadSource? Source { get; init; }
    public string? Notes { get; init; }
    public string? CloseReason { get; init; }
    public DateTime? ClosedDate { get; init; }
    public string AssignedToId { get; init; } = "";
    public string? AssignedToName { get; init; }
    public DateTime CreatedDate { get; init; }
    public DateTime? ModifiedDate { get; init; }
}

public class OpportunitySaveRequest
{
    [Display(Name = "Opportunity Name")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(150, MinimumLength = 2, ErrorMessage = ValidationMessages.LengthRange)]
    public string? OpportunityName { get; set; }

    [Display(Name = "Customer")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    public int? CustomerId { get; set; }

    [Display(Name = "Lead")]
    public int? LeadId { get; set; }

    [Display(Name = "Amount")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [Range(typeof(decimal), "0", "10000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = ValidationMessages.AmountNotNegative)]
    [PositiveWhenActive(nameof(Stage), nameof(OpportunityStage.Won), nameof(OpportunityStage.Lost))]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [ValidEnum]
    public OpportunityStage? Stage { get; set; } = OpportunityStage.Qualification;

    [Required(ErrorMessage = ValidationMessages.Required)]
    [Range(0, 100, ErrorMessage = ValidationMessages.Probability)]
    public int? Probability { get; set; } = 20;

    [Display(Name = "Expected Close Date")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [DataType(DataType.Date)]
    [NotBeforeToday(ValidationMessages.CloseDatePast, DependentProperty = nameof(Stage),
        InactiveValues = [nameof(OpportunityStage.Won), nameof(OpportunityStage.Lost)])]
    public DateTime? ExpectedCloseDate { get; set; }

    [ValidEnum]
    public LeadSource? Source { get; set; }

    [StringLength(2000, ErrorMessage = ValidationMessages.MaxLength)]
    public string? Notes { get; set; }

    [Display(Name = "Outcome / Close Reason")]
    [StringLength(500, ErrorMessage = ValidationMessages.MaxLength)]
    public string? CloseReason { get; set; }

    [Display(Name = "Assigned To")]
    [StringLength(450)]
    public string? AssignedToId { get; set; }
}

public sealed class OpportunityStageChangeRequest
{
    [Required(ErrorMessage = ValidationMessages.Required)]
    [ValidEnum]
    public OpportunityStage? Stage { get; set; }

    [StringLength(500, ErrorMessage = ValidationMessages.MaxLength)]
    public string? CloseReason { get; set; }
}
