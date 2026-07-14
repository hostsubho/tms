using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Models;
using Tms.Api.Services;
using StripeInvoice = Stripe.Invoice;
using StripeSubscription = Stripe.Subscription;
using StripeCheckoutSession = Stripe.Checkout.Session;

namespace Tms.Api.Controllers;

// Module 5.2 - Plans & Billing Administration. Inbound webhook from Stripe
// itself, not a browser or another part of this app - deliberately has NO
// authentication policy (Stripe can't carry a tenant JWT or an API key);
// the `Stripe-Signature` header + configured webhook secret is the entire
// trust boundary here, verified in IStripeService.ConstructWebhookEvent
// before any event data is trusted or acted on.
[ApiController]
[Route("api/webhooks/stripe")]
[AllowAnonymous]
public class StripeWebhookController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly IStripeService _stripe;

    public StripeWebhookController(TmsDbContext db, IStripeService stripe)
    {
        _db = db;
        _stripe = stripe;
    }

    [HttpPost]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(ct);

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = _stripe.ConstructWebhookEvent(json, Request.Headers["Stripe-Signature"].ToString());
        }
        catch (Stripe.StripeException)
        {
            // Signature didn't verify - could be a misconfigured webhook
            // secret, or someone probing this endpoint directly. Either way
            // the payload is untrusted and nothing here should touch the
            // database over it.
            return BadRequest();
        }

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync(stripeEvent, ct);
                break;
            case "invoice.paid":
                await UpsertInvoiceAsync(stripeEvent, InvoiceStatus.Paid, ct);
                break;
            case "invoice.payment_failed":
                // Module 5.2's "dunning status" bar: a failed payment marks
                // the tenant PastDue immediately - there's no background
                // worker in this deployment to run a grace-period sweep
                // (same lazy-evaluation constraint as SLA breach detection
                // elsewhere), so PastDue is the tenant's status from the
                // moment Stripe reports the failure until a human
                // (Super Admin) suspends or the tenant's card is updated
                // and a retry succeeds (invoice.paid arriving later flips
                // it back to Active).
                await UpsertInvoiceAsync(stripeEvent, InvoiceStatus.Open, ct);
                break;
            case "customer.subscription.deleted":
                await HandleSubscriptionDeletedAsync(stripeEvent, ct);
                break;
            default:
                // Unhandled event types are acknowledged (200) but ignored -
                // Stripe retries on any non-2xx response, and there's no
                // reason to fail the whole webhook delivery for an event
                // type this app doesn't act on (yet).
                break;
        }

        return Ok();
    }

    private async Task HandleCheckoutCompletedAsync(Stripe.Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not StripeCheckoutSession session) return;
        if (!Guid.TryParse(session.Metadata?.GetValueOrDefault("tenantId"), out var tenantId)) return;

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return;

        tenant.StripeCustomerId = session.CustomerId;
        tenant.StripeSubscriptionId = session.SubscriptionId;
        tenant.Status = TenantStatus.Active;

        if (session.SubscriptionId is not null)
        {
            var (priceId, currentPeriodEnd) = await _stripe.GetSubscriptionDetailsAsync(session.SubscriptionId, ct);
            tenant.CurrentPeriodEnd = currentPeriodEnd;

            // The specific Plan a Checkout Session was for isn't itself
            // round-tripped through Stripe metadata on the invoice/
            // subscription-deleted events below - matching back to a local
            // Plan by the Price actually being billed is the only
            // authoritative source once the subscription exists.
            if (priceId is not null)
            {
                var plan = await _db.Plans.FirstOrDefaultAsync(p => p.StripePriceId == priceId, ct);
                if (plan is not null) tenant.PlanId = plan.Id;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task UpsertInvoiceAsync(Stripe.Event stripeEvent, InvoiceStatus status, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not StripeInvoice stripeInvoice) return;
        if (stripeInvoice.CustomerId is null) return;

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.StripeCustomerId == stripeInvoice.CustomerId, ct);
        if (tenant is null) return;

        // Stripe can redeliver the same event more than once (its own
        // documented at-least-once guarantee) - upsert by StripeInvoiceId
        // rather than blindly inserting, so a redelivered `invoice.paid`
        // updates the existing row instead of creating a duplicate.
        var invoice = await _db.Invoices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.StripeInvoiceId == stripeInvoice.Id, ct);

        if (invoice is null)
        {
            invoice = new Invoice { Id = Guid.NewGuid(), TenantId = tenant.Id, StripeInvoiceId = stripeInvoice.Id };
            _db.Invoices.Add(invoice);
        }

        invoice.AmountDue = stripeInvoice.AmountDue / 100m;
        invoice.AmountPaid = stripeInvoice.AmountPaid / 100m;
        invoice.Currency = stripeInvoice.Currency ?? "usd";
        invoice.Status = status;
        invoice.PeriodStart = stripeInvoice.PeriodStart;
        invoice.PeriodEnd = stripeInvoice.PeriodEnd;
        invoice.HostedInvoiceUrl = stripeInvoice.HostedInvoiceUrl;
        invoice.StripeCreatedAt = stripeInvoice.Created;

        // Stripe does not guarantee in-order webhook delivery, especially on
        // retries - a late/redelivered `invoice.payment_failed` for an
        // *older* invoice arriving after a newer `invoice.paid` has already
        // been processed must not stomp the tenant back to PastDue. Only
        // the chronologically newest invoice Stripe has told this app about
        // (by the invoice's own Created timestamp, not by delivery order)
        // gets to decide the tenant's current Status/CurrentPeriodEnd - the
        // invoice row itself is still upserted either way, just not treated
        // as authoritative for tenant state if it's stale.
        var isLatestInvoiceForTenant = !await _db.Invoices.IgnoreQueryFilters()
            .Where(i => i.TenantId == tenant.Id && i.Id != invoice.Id)
            .AnyAsync(i => i.StripeCreatedAt > invoice.StripeCreatedAt, ct);

        if (isLatestInvoiceForTenant)
        {
            tenant.Status = status == InvoiceStatus.Paid ? TenantStatus.Active : TenantStatus.PastDue;
            if (status == InvoiceStatus.Paid)
            {
                tenant.CurrentPeriodEnd = stripeInvoice.PeriodEnd;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task HandleSubscriptionDeletedAsync(Stripe.Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not StripeSubscription subscription) return;
        if (subscription.CustomerId is null) return;

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.StripeCustomerId == subscription.CustomerId, ct);
        if (tenant is null) return;

        // Match on the specific subscription, not just the customer - a
        // tenant that already replaced this subscription with a newer one
        // (e.g. cancelled-and-resubscribed) must not be re-churned by a
        // late/redelivered event for the old, already-superseded
        // subscription.
        if (tenant.StripeSubscriptionId != subscription.Id) return;

        tenant.Status = TenantStatus.Churned;
        // Set once, never overwritten on a later resubscribe - see
        // Tenant.ChurnedAt's own comment for why.
        tenant.ChurnedAt ??= DateTime.UtcNow;

        // Cleared so a later ChangePlan call doesn't try to update a
        // subscription Stripe has already deleted (which would throw) -
        // the tenant is treated as never-subscribed until they check out
        // again, getting a fresh subscription and Customer relationship.
        tenant.StripeSubscriptionId = null;
        tenant.CurrentPeriodEnd = null;

        await _db.SaveChangesAsync(ct);
    }
}
