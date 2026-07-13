namespace Tms.Api.Models;

// Plans are platform-wide, not tenant-scoped - every tenant references one
// by PlanId. No CRUD API for these yet (Super Admin billing UI is future
// work, per Module 5.2); for now they're seeded directly (docs/seed-plans.sql).
public class Plan
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MaxAgents { get; set; }
    public int MaxTicketsPerMonth { get; set; }
    public decimal PriceMonthly { get; set; }
}
