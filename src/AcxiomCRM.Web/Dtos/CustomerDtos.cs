using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Validation;

namespace AcxiomCRM.Web.Dtos;

public sealed class CustomerQuery : ListQuery
{
    public CustomerStatus? Status { get; set; }
    public string? AssignedToId { get; set; }
}

public sealed class CustomerDto
{
    public int CustomerId { get; init; }
    public string CustomerCode { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? CompanyName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public CustomerStatus Status { get; init; }
    public string? Notes { get; init; }
    public string AssignedToId { get; init; } = "";
    public string? AssignedToName { get; init; }
    public int? SourceLeadId { get; init; }
    public DateTime CreatedDate { get; init; }
    public string? CreatedByName { get; init; }
    public DateTime? ModifiedDate { get; init; }
}

/// <summary>Create / update payload for a customer.</summary>
public class CustomerSaveRequest
{
    [Display(Name = "Customer Name")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(100, MinimumLength = 2, ErrorMessage = ValidationMessages.LengthRange)]
    public string? CustomerName { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(150, ErrorMessage = ValidationMessages.MaxLength)]
    [CrmEmail]
    public string? Email { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(15, ErrorMessage = ValidationMessages.MaxLength)]
    [CrmPhone]
    public string? Phone { get; set; }

    [Display(Name = "Company Name")]
    [StringLength(150, ErrorMessage = ValidationMessages.MaxLength)]
    public string? CompanyName { get; set; }

    [StringLength(250, ErrorMessage = ValidationMessages.MaxLength)]
    public string? Address { get; set; }

    [StringLength(100, ErrorMessage = ValidationMessages.MaxLength)]
    public string? City { get; set; }

    [StringLength(100, ErrorMessage = ValidationMessages.MaxLength)]
    public string? State { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [ValidEnum]
    public CustomerStatus? Status { get; set; } = CustomerStatus.Active;

    [StringLength(2000, ErrorMessage = ValidationMessages.MaxLength)]
    public string? Notes { get; set; }

    /// <summary>Owner. Optional: defaults to the caller. Must be an active user inside the caller's scope.</summary>
    [Display(Name = "Assigned To")]
    [StringLength(450)]
    public string? AssignedToId { get; set; }
}
