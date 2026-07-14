namespace Tms.Api.Models;

// Mirrors Stripe's own invoice status values closely enough for this app's
// purposes - Draft is deliberately omitted since a draft invoice is never
// something Stripe sends a webhook event for that this app needs to react
// to.
public enum InvoiceStatus
{
    Open,
    Paid,
    Uncollectible,
    Void,
}

// Module 5.2 - Plans & Billing Administration. A local, read-only mirror of
// Stripe Invoice objects, kept in sync via StripeWebhookController - this
// app never creates or modifies an invoice itself, only records what Stripe
// reports happened. Deliberately denormalized (no live join back to Stripe)
// so tenant/Super Admin invoice history pages load from Postgres, not a
// live Stripe API call on every page view.
public class Invoice
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string StripeInvoiceId { get; set; } = string.Empty;
    public decimal AmountDue { get; set; }
    public decimal AmountPaid { get; set; }
    public string Currency { get; set; } = "usd";
    public InvoiceStatus Status { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    // Stripe's own hosted invoice page - lets a tenant view/download a PDF
    // without this app needing to generate one itself.
    public string? HostedInvoiceUrl { get; set; }

    // Stripe's own `invoice.created` timestamp - distinct from CreatedAt
    // below (when *this row* was first written). Stripe explicitly does not
    // guarantee in-order webhook delivery, so StripeWebhookController uses
    // this to detect a late/redelivered event for an older invoice arriving
    // after a newer one has already been processed, and skips overwriting
    // the tenant's Status/CurrentPeriodEnd in that case (the invoice row
    // itself is still upserted either way - just not treated as the
    // tenant's current billing state).
    public DateTime StripeCreatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
