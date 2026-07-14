namespace Tms.Api.Models;

// Module 5.2 - Plans & Billing Administration. A record of a manually
// applied billing credit - the actual credit is a real Stripe customer
// balance adjustment (see IStripeService.ApplyCreditAsync), applied at the
// same time this row is written; this table exists purely so Super Admin
// staff can see a history of who applied what credit and why, since Stripe
// itself doesn't attribute a balance transaction to an internal staff
// member or a human-readable reason in the way this app needs.
public class BillingCredit
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public long AmountCents { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid CreatedByPlatformUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
