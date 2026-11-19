using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Web.Models;

public enum CustomerStatus
{
    Prospect,
    Active,
    Inactive
}

public enum LeadStatus
{
    New,
    Contacted,
    Qualified,
    Unqualified,
    Converted,
    Lost
}

public enum LeadSource
{
    Website,
    Referral,
    Campaign,
    [Display(Name = "Cold Call")] ColdCall,
    Event,
    [Display(Name = "Social Media")] SocialMedia,
    Partner,
    Other
}

public enum LeadPriority
{
    Low,
    Medium,
    High
}

public enum OpportunityStage
{
    Qualification,
    Proposal,
    Negotiation,
    Won,
    Lost
}

public enum OpportunityStatus
{
    Open,
    Won,
    Lost
}

public enum FollowUpType
{
    Call,
    Meeting,
    Email,
    Visit,
    Task
}

public enum FollowUpStatus
{
    Planned,
    Completed,
    Missed,
    Cancelled
}

public enum ActivityType
{
    Call,
    Meeting,
    Email,
    Task
}

public enum ActivityStatus
{
    Planned,
    Completed,
    Cancelled
}

/// <summary>The kind of CRM record a follow-up or activity is attached to.</summary>
public enum RelatedRecordType
{
    Customer,
    Lead,
    Opportunity
}

public enum AuditModule
{
    Authentication,
    Security,
    User,
    Customer,
    Lead,
    Opportunity,
    FollowUp,
    Activity,
    Report,
    System
}

public enum AuditAction
{
    Login,
    LoginFailed,
    Logout,
    Register,
    Lockout,
    Unlock,
    PasswordChange,
    PasswordReset,
    RoleChange,
    Activate,
    Deactivate,
    AccessDenied,
    Create,
    Update,
    Delete,
    StatusChange,
    Assign,
    Convert,
    Complete,
    Reschedule,
    Export,
    Seed
}

public enum AuditResult
{
    Success,
    Failure
}
