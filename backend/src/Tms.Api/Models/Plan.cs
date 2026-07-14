namespace Tms.Api.Models;

// Plans are platform-wide, not tenant-scoped - every tenant references one
// by PlanId. Originally seeded directly (docs/seed-plans.sql); Module 5.2
// adds a Super Admin CRUD surface (PlatformPlansController) on top of that
// seed data rather than replacing it.
public class Plan
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MaxAgents { get; set; }
    public int MaxTicketsPerMonth { get; set; }
    public decimal PriceMonthly { get; set; }

    // Module 5.2 - Plans & Billing Administration. The Stripe Price object
    // this plan bills against - null for a genuinely free plan (Free Trial),
    // which never goes through Checkout at all. Set via PlatformPlansController
    // once an Admin has created the matching Product/Price in the Stripe
    // Dashboard (or API) - this app never creates Stripe Products/Prices
    // itself, only references an id already created there, same reasoning
    // as never generating its own signing keys from thin air.
    public string? StripePriceId { get; set; }
}
