using Tms.Api.Dtos.Billing;

namespace Tms.Api.Dtos.Platform;

public record BillingCreditResponse(Guid Id, long AmountCents, string Reason, DateTime CreatedAt);

public record PlatformBillingOverviewResponse(
    Guid TenantId,
    string TenantName,
    Guid PlanId,
    string PlanName,
    string Status,
    bool HasBillingSetUp,
    DateTime? CurrentPeriodEnd,
    List<InvoiceResponse> Invoices,
    List<BillingCreditResponse> Credits);

public record ApplyCreditRequest(long AmountCents, string Reason);
public record OverridePlanRequest(Guid PlanId);

public record PlanDistributionEntry(Guid PlanId, string PlanName, int TenantCount, decimal Mrr);

public record RevenueResponse(
    decimal Mrr,
    decimal Arr,
    List<PlanDistributionEntry> PlanDistribution,
    int NewTenantsLast30Days,
    int ChurnedTenantsLast30Days);
