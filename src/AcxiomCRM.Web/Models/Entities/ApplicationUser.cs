using Microsoft.AspNetCore.Identity;

namespace AcxiomCRM.Web.Models.Entities;

/// <summary>
/// Application identity. Password hashes, security stamps, failed-login counters and lockout
/// end dates are managed by ASP.NET Core Identity (AccessFailedCount / LockoutEnd).
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Set when an administrator resets the password; forces a change at next sign-in.</summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? LastLoginDate { get; set; }

    /// <summary>Reporting manager. A manager's team is the set of users whose ManagerId is the manager.</summary>
    public string? ManagerId { get; set; }

    public ApplicationUser? Manager { get; set; }

    public ICollection<ApplicationUser> DirectReports { get; set; } = new List<ApplicationUser>();
}

/// <summary>Previous password hashes, used to block re-use of recent passwords.</summary>
public class PasswordHistory
{
    public long PasswordHistoryId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }
}
