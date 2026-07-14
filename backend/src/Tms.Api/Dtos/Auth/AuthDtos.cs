namespace Tms.Api.Dtos.Auth;

// TenantSlug maps to Tenant.Subdomain. Registration assumes the tenant
// already exists (created via Module 2 - Tenant Onboarding); this endpoint
// only creates a user within it. The tenant's very first user should be
// provisioned as Role.Admin by the onboarding flow, not by this endpoint.
public record RegisterRequest(string TenantSlug, string Email, string Password);

public record LoginRequest(string TenantSlug, string Email, string Password);

public record RefreshRequest(string RefreshToken);

// Module 12 - Roles & Permissions: Permissions mirrors exactly what's
// snapshotted into the JWT's own "permissions" claim (see JwtTokenService) -
// exposed here too so the frontend doesn't need to decode the token to know
// what a custom role grants when deciding what management UI to show.
// Always empty for Admin/Manager (they don't need it - see
// PermissionAuthorizationHandler, which grants them everything regardless).
public record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    Guid UserId,
    string Email,
    string Role,
    List<string> Permissions);
