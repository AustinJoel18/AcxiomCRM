namespace AcxiomCRM.Web.Infrastructure;

public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>IANA/Windows time zone used for "today" business rules (follow-up and close dates).</summary>
    public string TimeZoneId { get; set; } = "Asia/Kolkata";

    public bool EnableApiDocs { get; set; } = true;
}

public sealed class SecurityOptions
{
    public const string Section = "Security";

    public PasswordPolicyOptions Password { get; set; } = new();
    public LockoutPolicyOptions Lockout { get; set; } = new();

    public bool AllowSelfRegistration { get; set; } = true;

    /// <summary>Number of previous passwords that may not be re-used (0 disables the check).</summary>
    public int PasswordHistoryCount { get; set; } = 3;

    public int SessionTimeoutMinutes { get; set; } = 30;

    /// <summary>How often a cookie session is re-validated against the user's security stamp.</summary>
    public int SessionRevalidationMinutes { get; set; } = 1;

    public int LoginRateLimitPerMinute { get; set; } = 10;

    /// <summary>When true, Managers get a limited (team, business-only) view of the audit log.</summary>
    public bool ManagerCanViewAudit { get; set; } = true;
}

public sealed class PasswordPolicyOptions
{
    public int RequiredLength { get; set; } = 8;
    public bool RequireDigit { get; set; } = true;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireNonAlphanumeric { get; set; } = true;
    public int RequiredUniqueChars { get; set; } = 4;
}

public sealed class LockoutPolicyOptions
{
    public int MaxFailedAccessAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "AcxiomCRM";
    public string Audience { get; set; } = "AcxiomCRM.Api";

    /// <summary>Signing key (min 32 chars). Supply through user-secrets or the Jwt__Key environment variable.</summary>
    public string? Key { get; set; }

    public int AccessTokenMinutes { get; set; } = 60;
}

public sealed class CrmValidationOptions
{
    public const string Section = "Validation";

    /// <summary>Phone rule. Default: 10-digit Indian mobile number starting with 6-9.</summary>
    public string PhonePattern { get; set; } = ValidationDefaults.PhonePattern;

    public string PhoneHint { get; set; } = "10-digit mobile number, e.g. 9876543210";
}

public sealed class SeedOptions
{
    public const string Section = "Seed";

    public bool DemoData { get; set; }
    public string AdminEmail { get; set; } = "admin@acxiomcrm.local";
    public string? AdminPassword { get; set; }

    /// <summary>Password given to seeded demo users (development only).</summary>
    public string? DemoPassword { get; set; }
}

public static class ValidationDefaults
{
    public const string PhonePattern = @"^[6-9]\d{9}$";
    public const string EmailPattern = @"^[A-Za-z0-9._%+\-']+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$";
}
