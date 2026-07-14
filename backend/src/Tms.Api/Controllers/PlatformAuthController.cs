using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Platform;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Auth for the Super Admin console (Module 5). Entirely separate from
// /api/auth (tenant users) - PlatformUsers table has no TenantId, and tokens
// minted here carry "scope=platform_admin" instead of a tenant_id claim, so
// neither token type can be used to satisfy the other's endpoints.
[ApiController]
[Route("api/platform/auth")]
public class PlatformAuthController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly IPasswordHasher<PlatformUser> _passwordHasher;
    private readonly IJwtTokenService _tokenService;

    public PlatformAuthController(
        TmsDbContext db,
        IPasswordHasher<PlatformUser> passwordHasher,
        IJwtTokenService tokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    // Only usable while the PlatformUsers table is empty - creates the very
    // first Owner. Returns 409 once any platform user exists. Every
    // PlatformUser after this one goes through AddPlatformUser below
    // (Module 5.6), which requires an existing Owner's token.
    [HttpPost("bootstrap")]
    public async Task<ActionResult<PlatformAuthResponse>> Bootstrap([FromBody] BootstrapRequest request, CancellationToken ct)
    {
        var anyExist = await _db.PlatformUsers.AnyAsync(ct);
        if (anyExist)
        {
            return Conflict(new { message = "A platform owner already exists. Bootstrap can only run once." });
        }

        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email,
            Role = PlatformRole.Owner,
            CreatedAt = DateTime.UtcNow,
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _db.PlatformUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        var token = _tokenService.CreatePlatformAccessToken(user);
        return new PlatformAuthResponse(token.Token, token.ExpiresAtUtc, user.Id, user.Email, user.Role.ToString());
    }

    [HttpPost("login")]
    public async Task<ActionResult<PlatformAuthResponse>> Login([FromBody] PlatformLoginRequest request, CancellationToken ct)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive, ct);
        if (user is null)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var token = _tokenService.CreatePlatformAccessToken(user);
        return new PlatformAuthResponse(token.Token, token.ExpiresAtUtc, user.Id, user.Email, user.Role.ToString());
    }

    // Module 5.6 - the follow-up bootstrap's own comment pointed at: adding
    // platform users (any role, including a second Owner) once at least one
    // already exists. Owner-only (see "PlatformOwnerOnly" policy) - a
    // PlatformAdmin must not be able to create its own peers or a new Owner.
    [HttpPost("admins")]
    [Authorize(Policy = "PlatformOwnerOnly")]
    public async Task<ActionResult<PlatformUserResponse>> AddPlatformUser([FromBody] AddPlatformUserRequest request, CancellationToken ct)
    {
        var emailTaken = await _db.PlatformUsers.AnyAsync(u => u.Email == request.Email, ct);
        if (emailTaken)
        {
            return Conflict(new { message = "A platform user with that email already exists." });
        }

        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email,
            Role = request.Role,
            CreatedAt = DateTime.UtcNow,
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _db.PlatformUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        return new PlatformUserResponse(user.Id, user.Name, user.Email, user.Role.ToString(), user.IsActive, user.CreatedAt);
    }
}
