namespace Tms.Api.Services;

// Module 5.2 - Plans & Billing Administration. Thin wrapper around the
// Stripe.net SDK - every method that actually talks to Stripe's API lives
// here, nowhere else, so the controllers stay free of Stripe-specific types
// and a future provider swap (or a fake implementation for local dev
// without real Stripe credentials) only has to satisfy this interface.
public interface IStripeService
{
    Task<string> CreateCustomerAsync(Guid tenantId, string name, string adminEmail, CancellationToken ct);

    // Hosted Stripe Checkout, subscription mode - used the first time a
    // tenant subscribes to a paid plan (no existing Stripe subscription
    // yet). Returns the URL to redirect the browser to; the actual
    // plan/status change happens later, asynchronously, when Stripe calls
    // back to StripeWebhookController.
    Task<string> CreateCheckoutSessionAsync(
        Guid tenantId, string stripeCustomerId, string stripePriceId, string successUrl, string cancelUrl, CancellationToken ct);

    // Looks up which Price a subscription is currently billing against, plus
    // its current period end - used by StripeWebhookController's
    // checkout.session.completed handler to resolve the local Plan a
    // brand-new subscription corresponds to, since a Checkout Session's own
    // metadata doesn't carry which specific Price was ultimately used.
    Task<(string? PriceId, DateTime? CurrentPeriodEnd)> GetSubscriptionDetailsAsync(string stripeSubscriptionId, CancellationToken ct);

    // Changes the price on an *existing* subscription directly (with
    // proration) - used when a tenant is already subscribed and switching
    // between paid plans, where a brand-new Checkout Session would be the
    // wrong UX (they've already given a payment method).
    Task UpdateSubscriptionPriceAsync(string stripeSubscriptionId, string newStripePriceId, CancellationToken ct);

    // Cancels a subscription immediately - used when a tenant downgrades to
    // a genuinely free plan while an active paid Stripe subscription still
    // exists, so Stripe stops billing them for a plan the app no longer
    // grants. Without this, a downgrade would be local-only and Stripe
    // would keep charging every cycle for nothing.
    Task CancelSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct);

    // Hosted Stripe Billing Portal - lets a tenant update their payment
    // method, view/download past invoices, or cancel, without this app
    // building any of that UI itself.
    Task<string> CreateBillingPortalSessionAsync(string stripeCustomerId, string returnUrl, CancellationToken ct);

    // A negative-amount balance transaction on the Stripe Customer - reduces
    // what they owe on their next invoice. This is a real adjustment against
    // Stripe's own ledger, not a cosmetic local-only credit.
    Task ApplyCreditAsync(string stripeCustomerId, long amountCents, string reason, CancellationToken ct);

    // Verifies the `Stripe-Signature` header against the configured webhook
    // secret and parses the event - throws if verification fails, which
    // StripeWebhookController turns into a 400 rather than processing an
    // unverified payload.
    Stripe.Event ConstructWebhookEvent(string json, string signatureHeader);
}
