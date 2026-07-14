using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Billing;
using Tms.Api.Dtos.Platform;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 5.2 - Plans & Billing Administration, Super Admin side. Class-level
// PlatformAdmin policy (any platform role - Owner/PlatformAdmin/
// SupportEngineer/BillingAdmin/ReadOnlyAnalyst) covers the read endpoints,
// same convention as SuperAdminTenantsController; the two mutating actions
// (credit, plan override) additionally require the stricter PlatformBilling
// policy (Owner/PlatformAdmin/BillingAdmin - explicitly excludes
// SupportEngineer and ReadOnlyAnalyst, matching spec 5.6's "Billing Admin:
// billing only, no impersonation" framing).
[ApiController]
[Route("api/platform/billing")]
[Authorize(Policy = "PlatformAdmin")]
public class PlatformBillingController : ControllerBase
{
    // A single credit is capped well above any real plan's monthly price
    // (Enterprise is $999/mo) purely as a guardrail against a fat-fingered
    // or malicious amount - not a real business limit, just cheap insurance
    // on an action that directly moves Stripe's ledger.
    private const long MaxCreditCents = 50_000_00;

    private readonly TmsDbContext _db;
    private readonly IStripeService _stripe;
    private readonly IAuditLogService _auditLog;

    public PlatformBillingController(TmsDbContext db, IStripeService stripe, IAuditLogService auditLog)
    {
        _db = db;
        _stripe = stripe;
        _auditLog = auditLog;
    }

    [HttpGet("tenants/{tenantId:guid}")]
    public async Task<ActionResult<PlatformBillingOverviewResponse>> GetTenantBilling(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == tenant.PlanId, ct);

        // Tenants/Invoices/BillingCredits aren't scoped by the ambient
        // ITenantContext here (there's no logged-in tenant on a platform
        // token - TenantResolutionMiddleware never sets it for a
        // scope=platform_admin request), so the global query filter would
        // otherwise silently return nothing for every tenant.
        var invoices = await _db.Invoices.IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.PeriodStart)
            .Take(50)
            .ToListAsync(ct);

        var credits = await _db.BillingCredits.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return Ok(new PlatformBillingOverviewResponse(
            tenant.Id, tenant.Name, tenant.PlanId, plan?.Name ?? "(unknown plan)", tenant.Status.ToString(),
            tenant.StripeCustomerId is not null, tenant.CurrentPeriodEnd,
            invoices.Select(InvoiceResponse.FromEntity).ToList(),
            credits.Select(c => new BillingCreditResponse(c.Id, c.AmountCents, c.Reason, c.CreatedAt)).ToList()));
    }

    [HttpPost("tenants/{tenantId:guid}/credit")]
    [Authorize(Policy = "PlatformBilling")]
    public async Task<IActionResult> ApplyCredit(Guid tenantId, [FromBody] ApplyCreditRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        if (tenant.StripeCustomerId is null)
        {
            return BadRequest(new { message = "This tenant has no Stripe customer yet - nothing to credit." });
        }

        if (request.AmountCents <= 0)
        {
            return BadRequest(new { message = "amountCents must be a positive number." });
        }

        if (request.AmountCents > MaxCreditCents)
        {
            return BadRequest(new { message = $"amountCents exceeds the maximum single credit of {MaxCreditCents} cents." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { message = "A reason is required for every billing credit." });
        }

        await _stripe.ApplyCreditAsync(tenant.StripeCustomerId, request.AmountCents, request.Reason, ct);

        // GetUserId() reads "sub"/NameIdentifier defensively (whichever
        // claim type the JWT handler actually mapped it to) rather than
        // assuming one - same helper every other authenticated controller
        // in this app already uses, works identically for a PlatformUser
        // token since CreatePlatformAccessToken sets the same Sub claim.
        _db.BillingCredits.Add(new BillingCredit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AmountCents = request.AmountCents,
            Reason = request.Reason,
            CreatedByPlatformUserId = User.GetUserId(),
            CreatedAt = DateTime.UtcNow,
        });

        // This is the tenant's own audit trail (Module 5.4), not a
        // dedicated platform-wide one (that's noted as future work in the
        // README) - actorUserId is null and the label makes clear this came
        // from WMX staff, not the tenant's own team, same convention
        // PortalTicketsController uses for a portal customer's actions.
        _auditLog.Record(tenantId, actorUserId: null, $"Super Admin: {User.GetEmail()}", AuditAction.Updated,
            AuditEntityType.Billing, tenantId, $"Applied a ${request.AmountCents / 100m:F2} billing credit: {request.Reason}");

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("tenants/{tenantId:guid}/override-plan")]
    [Authorize(Policy = "PlatformBilling")]
    public async Task<IActionResult> OverridePlan(Guid tenantId, [FromBody] OverridePlanRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var overridePlan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, ct);
        if (overridePlan is null)
        {
            return BadRequest(new { message = "Unknown planId." });
        }

        // Deliberately does NOT touch Stripe - this is the "manual plan
        // override" the spec calls out separately from normal billing (a
        // negotiated/comped deal), so it must work even for a tenant with
        // no Stripe customer at all.
        tenant.PlanId = request.PlanId;

        _auditLog.Record(tenantId, actorUserId: null, $"Super Admin: {User.GetEmail()}", AuditAction.Updated,
            AuditEntityType.Billing, tenantId, $"Manually overrode plan to '{overridePlan.Name}' (no Stripe charge).");

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("revenue")]
    public async Task<ActionResult<RevenueResponse>> GetRevenue(CancellationToken ct)
    {
        // Computed on request, not pre-aggregated by a background job - same
        // lazy-evaluation constraint as the tenant-facing Reports module
        // (Module 9). Fine at today's tenant counts; would need real rollup
        // tables before this scales to a platform with thousands of tenants.
        var tenants = await _db.Tenants.ToListAsync(ct);
        var plans = await _db.Plans.ToListAsync(ct);
        var planLookup = plans.ToDictionary(p => p.Id);

        // MRR only counts tenants actually paying right now - Active status
        // with a real Stripe subscription. A Trial tenant or a manually
        // plan-overridden tenant on a paid-tier Plan.PriceMonthly is
        // deliberately excluded: they aren't contributing recurring revenue
        // this month even though their Plan has a nonzero list price.
        var payingTenants = tenants
            .Where(t => t.Status == TenantStatus.Active && t.StripeSubscriptionId is not null)
            .ToList();

        var mrr = payingTenants.Sum(t => planLookup.TryGetValue(t.PlanId, out var p) ? p.PriceMonthly : 0m);

        var distribution = payingTenants
            .GroupBy(t => t.PlanId)
            .Select(g =>
            {
                var plan = planLookup.GetValueOrDefault(g.Key);
                return new PlanDistributionEntry(g.Key, plan?.Name ?? "(unknown plan)", g.Count(), g.Count() * (plan?.PriceMonthly ?? 0m));
            })
            .OrderByDescending(d => d.Mrr)
            .ToList();

        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var newTenants30d = tenants.Count(t => t.CreatedAt >= thirtyDaysAgo);
        var churned30d = tenants.Count(t => t.ChurnedAt is not null && t.ChurnedAt >= thirtyDaysAgo);

        return Ok(new RevenueResponse(mrr, mrr * 12, distribution, newTenants30d, churned30d));
    }
}
