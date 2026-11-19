using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcxiomCRM.Web.Security;

/// <summary>Blocks sign-in for deactivated accounts (Identity returns NotAllowed).</summary>
public sealed class AppSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user) =>
        user.IsActive && await base.CanSignInAsync(user);
}

/// <summary>Adds display name and the "must change password" flag to the cookie principal.</summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaimTypes.FullName, user.FullName));
        if (user.MustChangePassword) identity.AddClaim(new Claim(AppClaimTypes.MustChangePassword, "true"));
        return identity;
    }
}

/// <summary>Rejects re-use of the current or the N most recent passwords.</summary>
public sealed class PasswordHistoryValidator(ApplicationDbContext db, IOptions<SecurityOptions> options) : IPasswordValidator<ApplicationUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var count = options.Value.PasswordHistoryCount;
        if (count <= 0 || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(user.Id)) return IdentityResult.Success;

        var hashes = await db.PasswordHistories.AsNoTracking().Where(h => h.UserId == user.Id)
            .OrderByDescending(h => h.CreatedDate).Take(count).Select(h => h.PasswordHash).ToListAsync();
        if (!string.IsNullOrEmpty(user.PasswordHash)) hashes.Add(user.PasswordHash);

        foreach (var hash in hashes)
        {
            if (manager.PasswordHasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed)
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "PasswordReuse",
                    Description = $"You cannot reuse any of your last {count} passwords."
                });
        }
        return IdentityResult.Success;
    }
}

public interface IPasswordHistoryService
{
    Task RecordAsync(ApplicationUser user, CancellationToken ct = default);
}

public sealed class PasswordHistoryService(ApplicationDbContext db, IBusinessClock clock, IOptions<SecurityOptions> options) : IPasswordHistoryService
{
    public async Task RecordAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.PasswordHash) || options.Value.PasswordHistoryCount <= 0) return;
        db.PasswordHistories.Add(new PasswordHistory { UserId = user.Id, PasswordHash = user.PasswordHash, CreatedDate = clock.UtcNow });
        await db.SaveChangesAsync(ct);

        var stale = await db.PasswordHistories.Where(h => h.UserId == user.Id).OrderByDescending(h => h.CreatedDate)
            .Skip(options.Value.PasswordHistoryCount).ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.PasswordHistories.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }
    }
}

public static class SecurityStamp
{
    /// <summary>The raw security stamp never leaves the server; tokens carry only its hash.</summary>
    public static string Hash(string? stamp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp ?? string.Empty)))[..32];
}
