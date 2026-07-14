namespace Tms.Api.Dtos.Platform;

// "Module Licensing" - client customization & module cost negotiation. See
// TenantModuleFlag/Tenant.ModuleBillingTotalOverrideCents for the underlying
// model and SuperAdminTenantsController for the CRUD surface.
public record ModuleFlagResponse(string ModuleKey, bool Enabled, long? MonthlyCostCents);

public record TenantModuleLicensingResponse(
    IReadOnlyList<ModuleFlagResponse> Modules,
    long BasePlanPriceMonthlyCents,
    // Base plan price plus every enabled module's MonthlyCostCents (nulls
    // treated as 0) - what the system would suggest charging before any
    // Owner negotiation.
    long SuggestedTotalCents,
    // Null unless an Owner/BillingAdmin has set a different final number for
    // this tenant (see UpdateBillingTotalOverrideRequest).
    long? TotalOverrideCents,
    // TotalOverrideCents if set, otherwise SuggestedTotalCents - what this
    // tenant is actually being asked to pay.
    long EffectiveTotalCents);

public record UpdateModuleFlagRequest(bool Enabled, long? MonthlyCostCents);

public record UpdateBillingTotalOverrideRequest(long? TotalOverrideCents);
