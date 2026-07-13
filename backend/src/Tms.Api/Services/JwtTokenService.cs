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

    public AccessTokenResult CreateAccessToken(AppUser user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("tenant_id", user.TenantId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        return BuildToken(claims);
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
