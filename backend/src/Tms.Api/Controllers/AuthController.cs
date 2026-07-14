using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Auth;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly IJwtTokenService _tokenService;

    public AuthController(
        TmsDbContext db,
        ITenantContext tenantContext,
        IPasswordHasher<AppUser> passwordHasher,
        IJwtTokenService tokenService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == request.TenantSlug, ct);
        if (tenant is null)
        {
            return NotFound(new { message = "Unknown tenant." });
        }

        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Churned)
        {
            return Forbid();
        }

        // Resolve tenant context for the pre-auth request, same mechanism
        // TenantResolutionMiddleware uses post-auth via the JWT claim.
        _tenantContext.TenantId = tenant.Id;

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email, ct);
        if (existing is not null)
        {
            return Conflict(new { message = "An account with this email already exists for this tenant." });
        }

        // First user in a tenant becomes Admin; everyone after defaults to Agent.
        // Real invite flows (Module 2) should let an Admin set the role explicitly.
        var isFirstUser = !await _db.Users.AnyAsync(ct);

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = request.Email,
            Role = isFirstUser ? Role.Admin : Role.Agent,
            CreatedAt = DateTime.UtcNow,
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == request.TenantSlug, ct);
        if (tenant is null)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Churned)
        {
            return Forbid();
        }

        _tenantContext.TenantId = tenant.Id;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive, ct);
        if (user is null || user.PasswordHash is null)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        return await IssueTokensAsync(user, ct);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);

        // Refresh tokens are looked up across tenants by hash first (we don't
        // know the tenant yet), then the tenant context is set from the result -
        // same pre-auth resolution pattern as Register/Login above.
        var stored = await _db.RefreshTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

        if (stored is null || !stored.IsActive)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        _tenantContext.TenantId = stored.TenantId;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId && u.IsActive, ct);
        if (user is null)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        stored.RevokedAt = DateTime.UtcNow;
        return await IssueTokensAsync(user, ct);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var stored = await _db.RefreshTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

        if (stored is not null)
        {
            stored.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    private async Task<AuthResponse> IssueTokensAsync(AppUser user, CancellationToken ct)
    {
        // Module 12 - Roles & Permissions: resolved here (not inside
        // JwtTokenService, which has no database access - see its comment)
        // and only queried at all if the user actually has a custom role,
        // which is the common-case-fast-path for every tenant that hasn't
        // adopted custom roles yet.
        var permissions = user.CustomRoleId is null
            ? Array.Empty<Permission>()
            : await _db.CustomRolePermissions
                .Where(p => p.CustomRoleId == user.CustomRoleId)
                .Select(p => p.Permission)
                .ToArrayAsync(ct);

        var accessToken = _tokenService.CreateAccessToken(user, permissions);
        var refreshTokenPlaintext = _tokenService.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            UserId = user.Id,
            TokenHash = _tokenService.HashToken(refreshTokenPlaintext),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        await _db.SaveChangesAsync(ct);

        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshTokenPlaintext,
            user.Id,
            user.Email,
            user.Role.ToString());
    }
}
