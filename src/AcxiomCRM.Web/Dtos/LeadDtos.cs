using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Validation;

namespace AcxiomCRM.Web.Dtos;

public sealed class LeadQuery : ListQuery
{
    public LeadStatus? Status { get; set; }
    public LeadSource? Source { get; set; }
    public string? AssignedToId { get; set; }
}

public sealed class LeadDto
{
    public int LeadId { get; init; }
    public string LeadCode { get; init; } = "";
    public string LeadName { get; init; } = "";
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? CompanyName { get; init; }
    public LeadSource Source { get; init; }
    public LeadStatus Status { get; init; }
    public LeadPriority Priority { get; init; }
    public decimal? ExpectedValue { get; init; }
    public string? Notes { get; init; }
    public string AssignedToId { get; init; } = "";
    public string? AssignedToName { get; init; }
    public DateTime CreatedDate { get; init; }
    public DateTime? ModifiedDate { get; init; }
    public DateTime? ConvertedDate { get; init; }
    public int? ConvertedCustomerId { get; init; }
    public string? ConvertedCustomerName { get; init; }
    public int? ConvertedOpportunityId { get; init; }
    public IReadOnlyList<LeadStatus> AllowedNextStatuses { get; init; } = [];
    public bool CanConvert { get; init; }
}

public class LeadSaveRequest : IValidatableObject
{
    [Display(Name = "Lead Name")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(100, MinimumLength = 2, ErrorMessage = ValidationMessages.LengthRange)]
    public string? LeadName { get; set; }

    [StringLength(150, ErrorMessage = ValidationMessages.MaxLength)]
    [CrmEmail]
    public string? Email { get; set; }

    [StringLength(15, ErrorMessage = ValidationMessages.MaxLength)]
    [CrmPhone]
    public string? Phone { get; set; }

    [Display(Name = "Company Name")]
    [StringLength(150, ErrorMessage = ValidationMessages.MaxLength)]
    public string? CompanyName { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [ValidEnum]
    public LeadSource? Source { get; set; }

    [Display(Name = "Lead Status")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [ValidEnum]
    public LeadStatus? Status { get; set; } = LeadStatus.New;

    [Required(ErrorMessage = ValidationMessages.Required)]
    [ValidEnum]
    public LeadPriority? Priority { get; set; } = LeadPriority.Medium;

    [Display(Name = "Expected Value")]
    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Expected Value must be between 0 and 100,000,000.")]
    public decimal? ExpectedValue { get; set; }

    [StringLength(2000, ErrorMessage = ValidationMessages.MaxLength)]
    public string? Notes { get; set; }

    [Display(Name = "Assigned To")]
    [StringLength(450)]
    public string? AssignedToId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Email) && string.IsNullOrWhiteSpace(Phone))
            yield return new ValidationResult("Provide at least an email address or a phone number.", [nameof(Email)]);
    }
}

public sealed class LeadStatusChangeRequest
{
    [Required(ErrorMessage = ValidationMessages.Required)]
    [ValidEnum]
    public LeadStatus? Status { get; set; }

    [StringLength(500, ErrorMessage = ValidationMessages.MaxLength)]
    public string? Remarks { get; set; }
}

/// <summary>Opportunity details captured while converting a lead.</summary>
public class LeadConvertOpportunity
{
    [Display(Name = "Opportunity Name")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(150, MinimumLength = 2, ErrorMessage = ValidationMessages.LengthRange)]
    public string? OpportunityName { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [Range(typeof(decimal), "0.01", "10000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = ValidationMessages.AmountPositive)]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [Range(0, 100, ErrorMessage = ValidationMessages.Probability)]
    public int? Probability { get; set; } = 20;

    [Display(Name = "Expected Close Date")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [DataType(DataType.Date)]
    [NotBeforeToday(ValidationMessages.CloseDatePast)]
    public DateTime? ExpectedCloseDate { get; set; }
}

public class LeadConvertRequest
{
    /// <summary>Link the lead to an existing customer instead of creating one.</summary>
    [Display(Name = "Existing Customer")]
    public int? ExistingCustomerId { get; set; }

    /// <summary>New customer details (required when ExistingCustomerId is not supplied).</summary>
    public CustomerSaveRequest? Customer { get; set; }

    /// <summary>Optional opportunity to open for the customer.</summary>
    public LeadConvertOpportunity? Opportunity { get; set; }
}

public sealed record LeadConvertResult(int LeadId, int CustomerId, string CustomerCode, int? OpportunityId);
