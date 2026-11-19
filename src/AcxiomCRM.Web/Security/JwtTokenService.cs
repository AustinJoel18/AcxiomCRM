using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models.Entities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AcxiomCRM.Web.Security;

public static class ApiAuthDefaults
{
    /// <summary>Policy scheme: Bearer token when an Authorization header is present, otherwise the Identity cookie.</summary>
    public const string Scheme = "ApiAuth";
}

/// <summary>
/// Provides the JWT signing key. The key must come from configuration (user-secrets / environment
/// variable Jwt__Key) — it is never committed. Development/Testing fall back to an ephemeral random key.
/// </summary>
public sealed class JwtKeyProvider
{
    public JwtKeyProvider(IOptions<JwtOptions> options, IHostEnvironment env, ILogger<JwtKeyProvider> logger)
    {
        var configured = options.Value.Key;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (configured.Length < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");
            Key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configured));
        }
        else if (env.IsDevelopment() || env.IsEnvironment("Testing"))
        {
            logger.LogWarning("Jwt:Key is not configured; using an ephemeral development key. API tokens will not survive restarts.");
            Key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
        }
        else
        {
            throw new InvalidOperationException("Jwt:Key must be configured (environment variable Jwt__Key) outside Development.");
        }
    }

    public SymmetricSecurityKey Key { get; }
}

public sealed class JwtTokenService(JwtKeyProvider keys, IOptions<JwtOptions> options, IBusinessClock clock)
{
    public (string Token, DateTime ExpiresAt, string Jti) Create(ApplicationUser user, IEnumerable<string> roles)
    {
        var o = options.Value;
        var jti = Guid.NewGuid().ToString("N");
        var expires = clock.UtcNow.AddMinutes(o.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, jti),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(AppClaimTypes.FullName, user.FullName),
            new(AppClaimTypes.SecurityStampHash, SecurityStamp.Hash(user.SecurityStamp))
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = clock.UtcNow,
            IssuedAt = clock.UtcNow,
            Expires = expires,
            SigningCredentials = new SigningCredentials(keys.Key, SecurityAlgorithms.HmacSha256)
        });
        return (token, expires, jti);
    }
}

/// <summary>Revoked token IDs (API logout). Backed by IDistributedCache so it can move to Redis unchanged.</summary>
public interface ITokenRevocationStore
{
    Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken ct = default);
    Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default);
}

public sealed class DistributedTokenRevocationStore(IDistributedCache cache) : ITokenRevocationStore
{
    public Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken ct = default) =>
        cache.SetStringAsync("revoked:" + jti, "1", new DistributedCacheEntryOptions { AbsoluteExpiration = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)).AddMinutes(5) }, ct);

    public async Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default) =>
        await cache.GetStringAsync("revoked:" + jti, ct) is not null;
}
