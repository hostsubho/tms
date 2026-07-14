namespace Tms.Api.Models;

public enum TenantStatus { Trial, Active, PastDue, Suspended, Churned }

// Client customization - theming. Light/Dark mirror the two color schemes
// most browsers/OSes already expose a preference for; Auto follows that
// system preference client-side rather than forcing one.
public enum ThemeMode { Light, Dark, Auto }

// A named scale (not a raw px value) so the picker stays a small, safe set
// of options rather than free-form input that could break layout math.
public enum ThemeBorderRadius { None, Small, Medium, Large }

// Controls spacing/line-height across the dashboard shell - Compact suits
// power users triaging a high ticket volume, Spacious suits a more relaxed,
// presentation-style use.
public enum ThemeDensity { Compact, Comfortable, Spacious }

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

    // Module 10 - Asset Management/CMDB, and Super Admin 5.1's "tenant-level
    // feature flags" bullet. Scoped down from a general per-tenant
    // feature-flag system (which would need a generic flag-name/value store)
    // to exactly the one flag the spec calls out by name ("turn on/off
    // specific modules (e.g., CMDB...) per tenant regardless of plan, for
    // pilots or negotiated deals") - defaults false so no existing tenant
    // suddenly gains a module it never asked for. Toggled by Owner/
    // PlatformAdmin via SuperAdminTenantsController, checked by
    // AssetsController on every request (not by a Plan check - the whole
    // point is this is independent of what plan the tenant is on).
    public bool CmdbEnabled { get; set; }

    // "Module Licensing" - client customization & module cost negotiation.
    // Null means "use the computed suggested total" (base Plan.PriceMonthly
    // plus every enabled TenantModuleFlag's MonthlyCostCents) - set means an
    // Owner/BillingAdmin has negotiated a different final number for this
    // specific client (see SuperAdminTenantsController.UpdateBillingTotalOverride).
    // Deliberately independent of the per-module MonthlyCostCents overrides
    // on TenantModuleFlag itself - an Owner can override the grand total, the
    // per-module price, or both at once; neither is derived from the other.
    public long? ModuleBillingTotalOverrideCents { get; set; }

    // Client customization - theming, extending the Module 2 setup-wizard
    // fields (LogoUrl/PrimaryColor) above with a fuller theme editor.
    // Defaults match what every tenant already effectively looks like today
    // (light, medium rounding, comfortable spacing, no extra colors/CSS) so
    // this migration needs no data backfill.
    public string? SecondaryColor { get; set; }
    public string? AccentColor { get; set; }
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Light;
    public ThemeBorderRadius BorderRadius { get; set; } = ThemeBorderRadius.Medium;
    public ThemeDensity Density { get; set; } = ThemeDensity.Comfortable;

    // Raw CSS injected into the tenant's own dashboard shell only (never the
    // Super Admin console or another tenant's dashboard) - size-capped at the
    // API layer (see TenantController.UpdateTheme), not here, since EF Core
    // property attributes don't enforce string length against Postgres
    // `text` columns the way a `varchar(n)` would.
    public string? CustomCss { get; set; }
}
