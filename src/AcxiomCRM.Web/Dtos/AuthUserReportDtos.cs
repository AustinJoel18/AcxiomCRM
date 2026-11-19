using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Validation;

namespace AcxiomCRM.Web.Dtos;

public sealed class LoginRequest
{
    [Display(Name = "Username or Email")]
    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(256, ErrorMessage = ValidationMessages.MaxLength)]
    public string? UserNameOrEmail { get; set; }

    [Required(ErrorMessage = ValidationMessages.Required)]
    [StringLength(128, ErrorMessage = ValidationMessages.MaxLength)]
    public string? Password { get; set; }
}

public sealed class LoginResponse
{
    public required string AccessToken { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public DateTime ExpiresAt { get; init; }
    public required UserSummaryDto User { get; init; }
}

/// <summary>User as exposed to UI/API. Never contains password hashes, security stamps or tokens.</summary>
public sealed class UserDto
{
    public string Id { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Email { get; init; } = "";
    public string UserName { get; init; } = "";
    public string? PhoneNumber { get; init; }
    public string? Role { get; init; }
    public bool IsActive { get; init; }
    public bool IsLockedOut { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public int FailedLoginCount { get; init; }
    public bool MustChangePassword { get; init; }
    public string? ManagerId { get; init; }
    public string? ManagerName { get; init; }
    public DateTime CreatedDate { get; init; }
    public DateTime? LastLoginDate { get; init; }
}

public sealed class UserQuery : ListQuery
{
    public string? Role { get; set; }

    /// <summary>"active", "inactive" or "locked".</summary>
    public string? State { get; set; }
}

public sealed class AuditQuery : ListQuery
{
    public string? UserId { get; set; }
    public AuditModule? Module { get; set; }
    public AuditAction? Action { get; set; }
    public AuditResult? Result { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? EntityName { get; set; }
    public string? RecordId { get; set; }
}

public sealed class AuditLogDto
{
    public long AuditLogId { get; init; }
    public DateTime CreatedDate { get; init; }
    public string? UserId { get; init; }
    public string? UserName { get; init; }
    public AuditModule Module { get; init; }
    public AuditAction Action { get; init; }
    public string? EntityName { get; init; }
    public string? RecordId { get; init; }
    public AuditResult Result { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Details { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}

public sealed record PipelineStageDto(OpportunityStage Stage, int Count, decimal Amount, decimal WeightedAmount);

public sealed record PipelineOwnerDto(string OwnerId, string OwnerName, int OpenCount, decimal OpenAmount, decimal WeightedAmount, int WonCount, decimal WonAmount, int LostCount);

public sealed class PipelineReportDto
{
    public required IReadOnlyList<PipelineStageDto> Stages { get; init; }
    public required IReadOnlyList<PipelineOwnerDto> Owners { get; init; }
    public decimal TotalOpenAmount { get; init; }
    public decimal TotalWeightedAmount { get; init; }
    public int OpenCount { get; init; }
    public DateTime GeneratedAt { get; init; }
    public string Scope { get; init; } = "";
}
