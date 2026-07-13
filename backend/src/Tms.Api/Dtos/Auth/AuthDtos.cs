namespace Tms.Api.Dtos.Auth;

// TenantSlug maps to Tenant.Subdomain. Registration assumes the tenant
// already exists (created via Module 2 - Tenant Onboarding); this endpoint
// only creates a user within it. The tenant's very first user should be
// provisioned as Role.Admin by the onboarding flow, not by this endpoint.
public record RegisterRequest(string TenantSlug, string Email, string Password);

public record LoginRequest(string TenantSlug, string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    Guid UserId,
    string Email,
    string Role);
