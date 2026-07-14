using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Tms.Api.Models;

namespace Tms.Api.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _config;

    public JwtTokenService(IConfiguration config)
    {
        _config = config;
    }

    public AccessTokenResult CreateAccessToken(AppUser user, IReadOnlyCollection<Permission>? permissions = null, string? impersonatorEmail = null)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("tenant_id", user.TenantId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        // Module 12 - Roles & Permissions: a custom role's granted
        // permissions are snapshotted into the token at issuance, same
        // tradeoff already accepted for the Role claim above - a
        // permission change takes effect on next login/refresh, not
        // instantly. Omitted entirely (rather than an empty claim) when
        // there's nothing to grant, so tokens for the common case (no
        // custom role) stay exactly as small as before this module existed.
        if (permissions is { Count: > 0 })
        {
            claims.Add(new Claim("permissions", string.Join(',', permissions.Select(p => p.ToString()))));
        }

        // Module 5.1 - Tenant impersonation. See this method's doc comment
        // on IJwtTokenService for why this is a plain extra claim rather
        // than a different token shape.
        if (impersonatorEmail is not null)
        {
            claims.Add(new Claim("imp", impersonatorEmail));
        }

        return BuildToken(claims.ToArray());
    }

    public AccessTokenResult CreatePlatformAccessToken(PlatformUser user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("scope", "platform_admin"),
            new Claim("platform_role", user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        return BuildToken(claims);
    }

    public AccessTokenResult CreatePortalCustomerAccessToken(PortalCustomer customer)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, customer.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, customer.Email),
            new Claim("scope", "portal_customer"),
            new Claim("tenant_id", customer.TenantId.ToString()),
            new Claim("customer_id", customer.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        return BuildToken(claims);
    }

    private AccessTokenResult BuildToken(Claim[] claims)
    {
        var signingKey = _config["Auth:SigningKey"]
            ?? throw new InvalidOperationException("Auth:SigningKey is not configured.");

        var expiresMinutes = _config.GetValue<int?>("Auth:AccessTokenMinutes") ?? 15;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiresMinutes);

        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Auth:Issuer"],
            audience: _config["Auth:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessTokenResult(tokenString, expiresAt);
    }

    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public string HashToken(string plaintextToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintextToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
