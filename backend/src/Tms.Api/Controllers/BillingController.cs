using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Billing;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 5.2 - Plans & Billing Administration, tenant-facing side. Read
// endpoints are open to any tenant staff (an Agent should be able to see
// what plan their org is on); the plan-change/portal actions are Admin-only,
// same authorization shape as TenantController's existing PATCH /me and
// POST /me/plan.
[ApiController]
[Route("api/billing")]
[Authorize(Policy = "TenantStaff")]
public class BillingController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IStripeService _stripe;
    private readonly IAuditLogService _auditLog;

    public BillingController(TmsDbContext db, ITenantContext tenantContext, IStripeService stripe, IAuditLogService auditLog)
    {
        _db = db;
        _tenantContext = tenantContext;
        _stripe = stripe;
        _auditLog = auditLog;
    }

    [HttpGet("subscription")]
    public async Task<ActionResult<SubscriptionResponse>> GetSubscription(CancellationToken ct)
    {
        var tenant = await GetOwnTenantAsync(ct);
        if (tenant is null) return NotFound();

        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == tenant.PlanId, ct);
        if (plan is null) return NotFound(new { message = "Tenant's plan no longer exists." });

        return Ok(new SubscriptionResponse(
            plan.Id, plan.Name, plan.PriceMonthly, tenant.Status.ToString(),
            tenant.CurrentPeriodEnd, tenant.StripeCustomerId is not null));
    }

    [HttpGet("invoices")]
    public async Task<ActionResult<IEnumerable<InvoiceResponse>>> GetInvoices(CancellationToken ct)
    {
        var invoices = await _db.Invoices
            .OrderByDescending(i => i.PeriodStart)
            .Take(100)
            .ToListAsync(ct);

        return Ok(invoices.Select(InvoiceResponse.FromEntity));
    }

    [HttpPost("change-plan")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ChangePlanResult>> ChangePlan([FromBody] ChangePlanRequest request, CancellationToken ct)
    {
        try
        {
            return await ChangePlanInternal(request, ct);
        }
        catch (Stripe.StripeException ex)
        {
            // A card decline on proration, an expired/invalid Stripe key, a
            // network blip talking to Stripe - all surface as a StripeException,
            // which without this catch would otherwise bubble up as a bare,
            // unhelpful 500. ex.StripeError?.Message is Stripe's own
            // human-readable explanation (e.g. "Your card was declined").
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = ex.StripeError?.Message ?? "Stripe couldn't complete this request. Please try again." });
        }
    }

    private async Task<ActionResult<ChangePlanResult>> ChangePlanInternal(ChangePlanRequest request, CancellationToken ct)
    {
        var tenant = await GetOwnTenantAsync(ct);
        if (tenant is null) return NotFound();

        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, ct);
        if (plan is null)
        {
            return BadRequest(new { message = "Unknown planId. Call GET /api/plans for valid options." });
        }

        // A genuinely free plan never *starts* a Stripe relationship, but a
        // tenant moving here FROM a paid plan may already have a live
        // subscription - it must be cancelled, not just ignored, otherwise
        // Stripe keeps billing every cycle for a plan the app no longer
        // grants. (A tenant that was never on a paid plan simply has no
        // StripeSubscriptionId, so this is a no-op for them.)
        if (plan.PriceMonthly <= 0)
        {
            if (tenant.StripeSubscriptionId is not null)
            {
                await _stripe.CancelSubscriptionAsync(tenant.StripeSubscriptionId, ct);
                tenant.StripeSubscriptionId = null;
                tenant.CurrentPeriodEnd = null;
            }
            tenant.PlanId = plan.Id;
            _auditLog.Record(tenant.Id, User.GetUserId(), User.GetEmail(), AuditAction.Updated,
                AuditEntityType.Billing, tenant.Id, $"Changed plan to '{plan.Name}' (no charge).");
            await _db.SaveChangesAsync(ct);
            return Ok(new ChangePlanResult(false, null, plan.Id));
        }

        if (string.IsNullOrEmpty(plan.StripePriceId))
        {
            return BadRequest(new { message = $"Plan '{plan.Name}' isn't wired up for billing yet (no Stripe price configured)." });
        }

        // Already has an active Stripe subscription (upgrading/downgrading
        // between two paid plans) - update the existing subscription's
        // price directly rather than sending them through Checkout again,
        // which already has their payment method on file.
        if (tenant.StripeSubscriptionId is not null)
        {
            await _stripe.UpdateSubscriptionPriceAsync(tenant.StripeSubscriptionId, plan.StripePriceId, ct);
            tenant.PlanId = plan.Id;
            _auditLog.Record(tenant.Id, User.GetUserId(), User.GetEmail(), AuditAction.Updated,
                AuditEntityType.Billing, tenant.Id, $"Changed plan to '{plan.Name}' via existing Stripe subscription.");
            await _db.SaveChangesAsync(ct);
            return Ok(new ChangePlanResult(false, null, plan.Id));
        }

        // First-ever paid subscription for this tenant - needs a Stripe
        // Customer if it doesn't already have one (e.g. a trial tenant that
        // never subscribed before).
        tenant.StripeCustomerId ??= await _stripe.CreateCustomerAsync(tenant.Id, tenant.Name, User.GetEmail(), ct);
        await _db.SaveChangesAsync(ct);

        var checkoutUrl = await _stripe.CreateCheckoutSessionAsync(
            tenant.Id, tenant.StripeCustomerId, plan.StripePriceId, request.SuccessUrl, request.CancelUrl, ct);

        // Deliberately does NOT set tenant.PlanId here - the plan only
        // actually changes once Stripe confirms payment via the
        // checkout.session.completed webhook (see StripeWebhookController).
        // Setting it optimistically here would grant paid-plan access to a
        // tenant that abandoned the Checkout page without paying.
        return Ok(new ChangePlanResult(true, checkoutUrl, null));
    }

    [HttpPost("portal-session")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PortalSessionResponse>> CreatePortalSession([FromBody] PortalSessionRequest request, CancellationToken ct)
    {
        var tenant = await GetOwnTenantAsync(ct);
        if (tenant is null) return NotFound();

        if (tenant.StripeCustomerId is null)
        {
            return BadRequest(new { message = "This workspace hasn't subscribed to a paid plan yet - there's nothing to manage." });
        }

        try
        {
            var url = await _stripe.CreateBillingPortalSessionAsync(tenant.StripeCustomerId, request.ReturnUrl, ct);
            return Ok(new PortalSessionResponse(url));
        }
        catch (Stripe.StripeException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = ex.StripeError?.Message ?? "Stripe couldn't complete this request. Please try again." });
        }
    }

    private async Task<Tenant?> GetOwnTenantAsync(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        return await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
    }
}
