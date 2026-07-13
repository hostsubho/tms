using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Portal;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Auth for the customer/end-user portal (Module 7). Entirely separate from
// /api/auth (tenant staff) and /api/platform/auth (Super Admin) - tokens
// minted here carry "scope=portal_customer" + tenant_id + customer_id, never
// a Role claim, so they can satisfy neither the staff [Authorize(Roles=...)]
// checks nor the PlatformAdmin/PlatformManage policies, and vice versa.
[ApiController]
[Route("api/portal/auth")]
public class PortalAuthController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher<PortalCustomer> _passwordHasher;
    private readonly IJwtTokenService _tokenService;

    public PortalAuthController(
        TmsDbContext db,
        ITenantContext tenantContext,
        IPasswordHasher<PortalCustomer> passwordHasher,
        IJwtTokenService tokenService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<PortalAuthResponse>> Register([FromBody] PortalRegisterRequest request, CancellationToken ct)
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

        // Pre-auth tenant resolution, same pattern as AuthController.Register.
        _tenantContext.TenantId = tenant.Id;

        var existing = await _db.PortalCustomers.FirstOrDefaultAsync(c => c.Email == request.Email, ct);
        if (existing is not null)
        {
            return Conflict(new { message = "An account with this email already exists for this tenant's portal." });
        }

        var customer = new PortalCustomer
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = request.Name,
            Email = request.Email,
            CreatedAt = DateTime.UtcNow,
        };
        customer.PasswordHash = _passwordHasher.HashPassword(customer, request.Password);

        _db.PortalCustomers.Add(customer);
        await _db.SaveChangesAsync(ct);

        var token = _tokenService.CreatePortalCustomerAccessToken(customer);
        return PortalAuthResponse.FromToken(token, customer);
    }

    [HttpPost("login")]
    public async Task<ActionResult<PortalAuthResponse>> Login([FromBody] PortalLoginRequest request, CancellationToken ct)
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

        var customer = await _db.PortalCustomers.FirstOrDefaultAsync(c => c.Email == request.Email && c.IsActive, ct);
        if (customer is null)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(customer, customer.PasswordHash, request.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var token = _tokenService.CreatePortalCustomerAccessToken(customer);
        return PortalAuthResponse.FromToken(token, customer);
    }
}
