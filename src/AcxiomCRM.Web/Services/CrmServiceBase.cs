using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Validation;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Web.Services;

/// <summary>Shared plumbing for CRM services: validation, assignment checks and code generation.</summary>
public abstract class CrmServiceBase(ApplicationDbContext db, IDataScope scope, IRequestValidator validator, IBusinessClock clock)
{
    protected ApplicationDbContext Db { get; } = db;
    protected IDataScope Scope { get; } = scope;
    protected IRequestValidator Validator { get; } = validator;
    protected IBusinessClock Clock { get; } = clock;

    public const string AssignmentError = "Select an active user within your permitted scope.";

    /// <summary>
    /// Resolves the owner for a create/update. Empty → keep current owner (update) or the caller (create).
    /// Returns null + error when the requested assignee is outside the caller's scope or inactive.
    /// </summary>
    protected async Task<(string? AssigneeId, string? Error)> ResolveAssigneeAsync(string? requested, string? currentOwner, CancellationToken ct)
    {
        var target = string.IsNullOrWhiteSpace(requested) ? currentOwner ?? Scope.UserId : requested.Trim();
        if (target == currentOwner) return (target, null); // unchanged owner is always allowed
        return await Scope.CanAssignToAsync(target, ct) ? (target, null) : (null, AssignmentError);
    }

    protected static string NextCode(string prefix, int? maxId) => $"{prefix}-{(maxId ?? 0) + 1:D5}";

    protected static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true;
}
