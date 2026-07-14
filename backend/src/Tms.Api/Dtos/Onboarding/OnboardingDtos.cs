namespace Tms.Api.Dtos.Onboarding;

// The self-serve signup flow (Module 2) - creates a Tenant and its first
// Admin user in a single call and logs them straight in, so "signup to
// working workspace" is one request, not a multi-step admin-assisted process.
public record SelfServeSignupRequest(
    string CompanyName,
    string Subdomain,
    Guid PlanId,
    string AdminEmail,
    string AdminPassword,
    string? TimeZone = "UTC");

public record PlanResponse(Guid Id, string Name, int MaxAgents, int MaxTicketsPerMonth, decimal PriceMonthly);

public record TenantSettingsResponse(
    Guid Id,
    string Name,
    string Subdomain,
    string TimeZone,
    string? LogoUrl,
    string? PrimaryColor,
    Guid PlanId,
    string Status,
    DateTime? TrialEndsAt,
    // Module 10 - Asset Management/CMDB. Exposed here (not just enforced
    // server-side by AssetsController) so the tenant dashboard can decide
    // whether to show the "Assets" nav link at all, rather than showing it
    // unconditionally and letting every non-CMDB tenant hit a 403.
    bool CmdbEnabled);

public record UpdateTenantSettingsRequest(string? Name, string? TimeZone, string? LogoUrl, string? PrimaryColor);

public record ChangePlanRequest(Guid PlanId);
