using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AcxiomCRM.Web.ViewModels;

// ------------------------------------------------------------------ Account

public sealed class LoginViewModel
{
    [Display(Name = "Username or Email")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(256, ErrorMessage = ValidationMessages.MaxLength)]
    public string? UserNameOrEmail { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(128, ErrorMessage = ValidationMessages.MaxLength)]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Display(Name = "Full Name")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(100, MinimumLength = 2, ErrorMessage = ValidationMessages.LengthRange)]
    public string? FullName { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(150, ErrorMessage = ValidationMessages.MaxLength)]
    [CrmEmail]
    public string? Email { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(128, MinimumLength = 8, ErrorMessage = ValidationMessages.LengthRange)]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Display(Name = "Confirm Password")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string? ConfirmPassword { get; set; }
}

public sealed class ChangePasswordViewModel
{
    [Display(Name = "Current Password")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [DataType(DataType.Password)]
    public string? CurrentPassword { get; set; }

    [Display(Name = "New Password")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(128, MinimumLength = 8, ErrorMessage = ValidationMessages.LengthRange)]
    [DataType(DataType.Password)]
    public string? NewPassword { get; set; }

    [Display(Name = "Confirm New Password")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string? ConfirmPassword { get; set; }

    [BindNever] public bool IsForced { get; set; }
}

// ------------------------------------------------------------------ Shared

public sealed class ListPageViewModel<TItem, TQuery>
{
    public required TQuery Query { get; init; }
    public required PagedResult<TItem> Results { get; init; }
    public IEnumerable<SelectListItem> Owners { get; init; } = [];
    public bool CanDelete { get; init; }
    public string Scope { get; init; } = "";
}

public sealed class ErrorViewModel
{
    public string? RequestId { get; init; }
    public int StatusCode { get; init; } = 500;
    public string Title { get; init; } = "Something went wrong";
    public string Message { get; init; } = "An unexpected error occurred. Please try again.";
}

public sealed class SearchViewModel
{
    public string? Q { get; init; }
    public IReadOnlyList<CustomerDto> Customers { get; init; } = [];
    public IReadOnlyList<LeadDto> Leads { get; init; } = [];
    public IReadOnlyList<OpportunityDto> Opportunities { get; init; } = [];
}

public sealed class DashboardViewModel
{
    public required DashboardData Data { get; init; }
    public required string ChartJson { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsManager { get; init; }
    public bool IsSales { get; init; }
}

// ------------------------------------------------------------------ Forms (inherit API request DTOs → identical validation rules)

public sealed class CustomerFormViewModel : CustomerSaveRequest
{
    [BindNever] public int? CustomerId { get; set; }
    [BindNever] public string? CustomerCode { get; set; }
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Owners { get; set; } = [];
}

public sealed class LeadFormViewModel : LeadSaveRequest
{
    [BindNever] public int? LeadId { get; set; }
    [BindNever] public string? LeadCode { get; set; }
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Owners { get; set; } = [];
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> StatusOptions { get; set; } = [];
}

public sealed class OpportunityFormViewModel : OpportunitySaveRequest
{
    [BindNever] public int? OpportunityId { get; set; }
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Owners { get; set; } = [];
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Customers { get; set; } = [];
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Leads { get; set; } = [];
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> StageOptions { get; set; } = [];
}

public sealed class FollowUpFormViewModel : FollowUpCreateRequest
{
    [BindNever] public int? FollowUpId { get; set; }

    /// <summary>"Customer:12", "Lead:5" or "Opportunity:3" (single grouped dropdown).</summary>
    [Display(Name = "Related To")]
    public string? RelatedKey { get; set; }

    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Owners { get; set; } = [];
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> RelatedOptions { get; set; } = [];
}

public sealed class ActivityFormViewModel : ActivitySaveRequest
{
    [BindNever] public int? ActivityId { get; set; }

    [Display(Name = "Related To")]
    public string? RelatedKey { get; set; }

    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Owners { get; set; } = [];
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> RelatedOptions { get; set; } = [];
}

public sealed class LeadConvertViewModel
{
    [BindNever, ValidateNever] public LeadDto Lead { get; set; } = new();

    [Display(Name = "Link to an existing customer")]
    public bool UseExistingCustomer { get; set; }

    [Display(Name = "Existing Customer")]
    public int? ExistingCustomerId { get; set; }

    public CustomerSaveRequest Customer { get; set; } = new();

    [Display(Name = "Create an opportunity")]
    public bool CreateOpportunity { get; set; } = true;

    public LeadConvertOpportunity Opportunity { get; set; } = new();

    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Customers { get; set; } = [];
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Owners { get; set; } = [];
}

public sealed class UserFormViewModel : UserCreateRequest
{
    [BindNever] public string? Id { get; set; }
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Managers { get; set; } = [];
    [BindNever, ValidateNever] public IEnumerable<SelectListItem> Roles { get; set; } = [];
}

// ------------------------------------------------------------------ Details pages

public sealed class CustomerDetailsViewModel
{
    public required CustomerDto Customer { get; init; }
    public IReadOnlyList<OpportunityDto> Opportunities { get; init; } = [];
    public IReadOnlyList<FollowUpDto> FollowUps { get; init; } = [];
    public IReadOnlyList<ActivityDto> Activities { get; init; } = [];
    public IReadOnlyList<AuditLogDto> History { get; init; } = [];
    public LeadDto? SourceLead { get; init; }
    public bool CanDelete { get; init; }
}

public sealed class LeadDetailsViewModel
{
    public required LeadDto Lead { get; init; }
    public IReadOnlyList<FollowUpDto> FollowUps { get; init; } = [];
    public IReadOnlyList<ActivityDto> Activities { get; init; } = [];
    public IReadOnlyList<AuditLogDto> History { get; init; } = [];
    public LeadStatusChangeRequest StatusChange { get; init; } = new();
    public bool CanDelete { get; init; }
}

public sealed class OpportunityDetailsViewModel
{
    public required OpportunityDto Opportunity { get; init; }
    public IReadOnlyList<FollowUpDto> FollowUps { get; init; } = [];
    public IReadOnlyList<ActivityDto> Activities { get; init; } = [];
    public IReadOnlyList<AuditLogDto> History { get; init; } = [];
    public OpportunityStageChangeRequest StageChange { get; init; } = new();
    public bool CanDelete { get; init; }
}

public sealed class FollowUpDetailsViewModel
{
    public required FollowUpDto FollowUp { get; init; }
    public IReadOnlyList<AuditLogDto> History { get; init; } = [];
    public FollowUpCompleteRequest Complete { get; init; } = new();
    public FollowUpRescheduleRequest Reschedule { get; init; } = new();
    public bool CanDelete { get; init; }
}

public sealed class ActivityDetailsViewModel
{
    public required ActivityDto Activity { get; init; }
    public IReadOnlyList<AuditLogDto> History { get; init; } = [];
    public bool CanDelete { get; init; }
}

public sealed class UserDetailsViewModel
{
    public required UserDto User { get; init; }
    public IReadOnlyList<AuditLogDto> History { get; init; } = [];
    public AdminResetPasswordRequest ResetPassword { get; init; } = new();
    public bool CanManage { get; init; }
    public bool IsSelf { get; init; }
}

public sealed class RolesViewModel
{
    public IReadOnlyList<RoleSummaryDto> Roles { get; init; } = [];
    public IReadOnlyList<(string Module, string Admin, string Manager, string Sales)> Matrix { get; init; } = [];
}

public sealed class AuditIndexViewModel
{
    public required AuditQuery Query { get; init; }
    public required PagedResult<AuditLogDto> Results { get; init; }
    public IEnumerable<SelectListItem> Users { get; init; } = [];
    public bool Limited { get; init; }
}

// ------------------------------------------------------------------ Reports

public sealed class ReportPageViewModel
{
    public required ReportTable Table { get; init; }
    public required ReportFilter Filter { get; init; }
    public IEnumerable<SelectListItem> Owners { get; init; } = [];
}

public sealed class PipelineReportViewModel
{
    public required PipelineReportDto Report { get; init; }
    public required ReportFilter Filter { get; init; }
    public IEnumerable<SelectListItem> Owners { get; init; } = [];
    public required string ChartJson { get; init; }
}

public sealed class ConversionReportViewModel
{
    public required ConversionReportDto Report { get; init; }
    public required ReportFilter Filter { get; init; }
    public IEnumerable<SelectListItem> Owners { get; init; } = [];
}

public sealed record ReportLink(string Action, string Title, string Description, string Icon, string Access);
