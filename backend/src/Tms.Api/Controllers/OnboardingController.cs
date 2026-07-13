using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Auth;
using Tms.Api.Dtos.Onboarding;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 2 - Tenant Onboarding & Workspace Setup. Self-serve signup: a new
// company goes from nothing to a working, logged-in workspace in one call -
// this is deliberately separate from the Super Admin "create tenant" flow
// (Module 5.1), which is for sales-assisted/manual provisioning instead.
[ApiController]
[Route("api/onboarding")]
public class OnboardingController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly IJwtTokenService _tokenService;

    public OnboardingController(
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

    [HttpPost("signup")]
    public async Task<ActionResult<AuthResponse>> Signup([FromBody] SelfServeSignupRequest request, CancellationToken ct)
    {
        var subdomainTaken = await _db.Tenants.AnyAsync(t => t.Subdomain == request.Subdomain, ct);
        if (subdomainTaken)
        {
            return Conflict(new { message = "That subdomain is already taken." });
        }

        var planExists = await _db.Plans.AnyAsync(p => p.Id == request.PlanId, ct);
        if (!planExists)
        {
            return BadRequest(new { message = "Unknown planId. Call GET /api/plans for valid options." });
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.CompanyName,
            Subdomain = request.Subdomain,
            PlanId = request.PlanId,
            Status = TenantStatus.Trial,
            TimeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? "UTC" : request.TimeZone,
            CreatedAt = DateTime.UtcNow,
            TrialEndsAt = DateTime.UtcNow.AddDays(14),
        };
        _db.Tenants.Add(tenant);

        // Same pre-auth tenant-context-resolution pattern as AuthController -
        // set it explicitly since there's no JWT yet to resolve it from.
        _tenantContext.TenantId = tenant.Id;

        var admin = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = request.AdminEmail,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow,
        };
        admin.PasswordHash = _passwordHasher.HashPassword(admin, request.AdminPassword);
        _db.Users.Add(admin);

        await _db.SaveChangesAsync(ct);

        var accessToken = _tokenService.CreateAccessToken(admin);
        var refreshTokenPlaintext = _tokenService.GenerateRefreshToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = admin.Id,
            TokenHash = _tokenService.HashToken(refreshTokenPlaintext),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        await _db.SaveChangesAsync(ct);

        var response = new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshTokenPlaintext,
            admin.Id,
            admin.Email,
            admin.Role.ToString());

        // Plain 201 with no Location header - there's no "get my signup by id"
        // route to point at, unlike a typical CreatedAtAction resource.
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
