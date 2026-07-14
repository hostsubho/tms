namespace Tms.Api.Models;

public enum TenantStatus { Trial, Active, PastDue, Suspended, Churned }

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TrialEndsAt { get; set; }

    // Setup-wizard fields (Module 2). Nullable/defaulted so existing rows
    // (seeded before this migration) don't need a backfill.
    public string TimeZone { get; set; } = "UTC";
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }

    // Module 5.2 - Plans & Billing Administration. Null until the tenant's
    // first Stripe Checkout Session completes (see StripeWebhookController) -
    // a brand-new trial or a manually-provisioned/comped tenant (Super Admin
    // "plan override") may never have either set, which is fine: PlanId and
    // Status are still the source of truth for what the tenant can do,
    // Stripe is only consulted for tenants actually paying through it.
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    // Mirrors Stripe's own `current_period_end` on the subscription - when
    // the next invoice is expected. Updated from the `invoice.paid` webhook
    // event, not computed locally (Stripe is the source of truth for billing
    // cycle timing, including trial/proration edge cases this app shouldn't
    // have to re-derive).
    public DateTime? CurrentPeriodEnd { get; set; }

    // Set once, the first time a `customer.subscription.deleted` webhook
    // event is processed for this tenant (see StripeWebhookController) - used
    // for the revenue dashboard's 30-day churn count. Never cleared or
    // reused even if the tenant later resubscribes, since that would show up
    // as a *new* Stripe subscription anyway.
    public DateTime? ChurnedAt { get; set; }
}
