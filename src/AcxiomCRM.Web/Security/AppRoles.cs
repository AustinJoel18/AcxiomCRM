namespace AcxiomCRM.Web.Security;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string SalesExecutive = "SalesExecutive";

    public static readonly string[] All = [Admin, Manager, SalesExecutive];

    public static string DisplayName(string? role) => role switch
    {
        SalesExecutive => "Sales Executive",
        null or "" => "—",
        _ => role
    };

    public static string Description(string role) => role switch
    {
        Admin => "Full application administration, users/roles, audit logs, configuration, all CRM records and reports.",
        Manager => "Team customer/lead/opportunity/follow-up management and management reports; no security administration.",
        SalesExecutive => "Assigned customers, leads, opportunities, follow-ups and activities; personal dashboards and reports.",
        _ => string.Empty
    };
}

public static class Policies
{
    /// <summary>Any signed-in user holding one of the three CRM roles.</summary>
    public const string CrmUser = nameof(CrmUser);
    public const string AdminOnly = nameof(AdminOnly);
    public const string ManagerOrAdmin = nameof(ManagerOrAdmin);
    public const string CanDeleteRecords = nameof(CanDeleteRecords);

    /// <summary>Admin (full) or Manager (view-only, own team).</summary>
    public const string UserDirectory = nameof(UserDirectory);

    /// <summary>Admin (full) or Manager (limited) when Security:ManagerCanViewAudit is enabled.</summary>
    public const string AuditViewer = nameof(AuditViewer);
}

public static class AppClaimTypes
{
    public const string FullName = "full_name";
    public const string MustChangePassword = "must_change_password";
    public const string SecurityStampHash = "sstamp_hash";
}
