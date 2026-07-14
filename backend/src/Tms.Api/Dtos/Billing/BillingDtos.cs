using Tms.Api.Models;

namespace Tms.Api.Dtos.Billing;

public record SubscriptionResponse(
    Guid PlanId,
    string PlanName,
    decimal PriceMonthly,
    string Status,
    DateTime? CurrentPeriodEnd,
    bool HasBillingSetUp);

public record ChangePlanRequest(Guid PlanId, string SuccessUrl, string CancelUrl);

// Exactly one of RedirectUrl/UpdatedPlanId is populated - RedirectUrl for a
// brand-new paid subscription (send the browser to Stripe Checkout, the
// actual plan change happens later via webhook), UpdatedPlanId when the
// change was applied immediately (switching to the free plan, or updating
// an existing paid subscription's price directly).
public record ChangePlanResult(bool RequiresRedirect, string? RedirectUrl, Guid? UpdatedPlanId);

public record PortalSessionRequest(string ReturnUrl);
public record PortalSessionResponse(string Url);

public record InvoiceResponse(
    Guid Id,
    string StripeInvoiceId,
    decimal AmountDue,
    decimal AmountPaid,
    string Currency,
    string Status,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string? HostedInvoiceUrl,
    DateTime CreatedAt)
{
    public static InvoiceResponse FromEntity(Invoice i) => new(
        i.Id, i.StripeInvoiceId, i.AmountDue, i.AmountPaid, i.Currency, i.Status.ToString(),
        i.PeriodStart, i.PeriodEnd, i.HostedInvoiceUrl, i.CreatedAt);
}
