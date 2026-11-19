using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Web.Validation;

namespace AcxiomCRM.Web.Dtos;

public class UserUpdateRequest
{
    [Display(Name = "Full Name")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(100, MinimumLength = 2, ErrorMessage = ValidationMessages.LengthRange)]
    public string? FullName { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(150, ErrorMessage = ValidationMessages.MaxLength)]
    [CrmEmail]
    public string? Email { get; set; }

    [Display(Name = "Phone")]
    [StringLength(15, ErrorMessage = ValidationMessages.MaxLength)]
    [CrmPhone]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(30)]
    public string? Role { get; set; }

    /// <summary>Reporting manager (Sales Executives only).</summary>
    [Display(Name = "Reporting Manager")]
    [StringLength(450)]
    public string? ManagerId { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class UserCreateRequest : UserUpdateRequest
{
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(128, MinimumLength = 8, ErrorMessage = ValidationMessages.LengthRange)]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Display(Name = "Confirm Password")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string? ConfirmPassword { get; set; }

    [Display(Name = "Require password change at first sign-in")]
    public bool MustChangePassword { get; set; } = true;
}

public class AdminResetPasswordRequest
{
    [Display(Name = "New Password")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(128, MinimumLength = 8, ErrorMessage = ValidationMessages.LengthRange)]
    [DataType(DataType.Password)]
    public string? NewPassword { get; set; }

    [Display(Name = "Confirm Password")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string? ConfirmPassword { get; set; }

    [Display(Name = "Require password change at next sign-in")]
    public bool RequireChange { get; set; } = true;
}

public sealed record RoleSummaryDto(string Name, string DisplayName, string Description, int UserCount, int ActiveUserCount);
