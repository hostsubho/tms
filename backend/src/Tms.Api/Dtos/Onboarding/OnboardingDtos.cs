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
    bool CmdbEnabled,
    // "Module Licensing" - every ModuleKey this tenant currently has
    // enabled (see IModuleAccessService), so the dashboard nav can hide
    // links for anything an Owner has switched off instead of showing them
    // unconditionally and letting the request 403 (same reasoning as
    // CmdbEnabled above, generalized to every optional module).
    IReadOnlyList<string> EnabledModules,
    // Client customization - theming (see Tenant's own doc comments).
    string? SecondaryColor,
    string? AccentColor,
    string ThemeMode,
    string BorderRadius,
    string Density,
    string? CustomCss);

public record UpdateTenantSettingsRequest(string? Name, string? TimeZone, string? LogoUrl, string? PrimaryColor);

// Client customization - theming. A separate request DTO from
// UpdateTenantSettingsRequest (not folded into it) since this is a distinct
// concern edited from its own settings page - keeps each request small and
// avoids one giant "update everything about the tenant" endpoint. All
// fields optional/omittable so the frontend can send just what changed.
public record UpdateThemeRequest(
    string? PrimaryColor,
    string? SecondaryColor,
    string? AccentColor,
    string? ThemeMode,
    string? BorderRadius,
    string? Density,
    string? CustomCss);

public record ChangePlanRequest(Guid PlanId);
