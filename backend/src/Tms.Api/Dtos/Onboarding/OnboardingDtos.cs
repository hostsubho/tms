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
    DateTime? TrialEndsAt);

public record UpdateTenantSettingsRequest(string? Name, string? TimeZone, string? LogoUrl, string? PrimaryColor);

public record ChangePlanRequest(Guid PlanId);
