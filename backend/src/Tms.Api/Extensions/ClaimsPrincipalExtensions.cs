using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Tms.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;

        return Guid.TryParse(sub, out var id)
            ? id
            : throw new InvalidOperationException("Request is missing a valid user id claim.");
    }

    // Module 5.4 - Audit Logging. All three token types (staff AppUser,
    // platform admin, portal customer - see JwtTokenService) carry the
    // standard Email claim, so this is safe to call from any authenticated
    // controller action regardless of which policy authorized it. Falls back
    // to a generic label rather than throwing - an audit entry missing a
    // precise actor is still far more useful than a request failing outright
    // over a cosmetic display detail.
    public static string GetEmail(this ClaimsPrincipal user) =>
        user.FindFirst(JwtRegisteredClaimNames.Email)?.Value
        ?? user.FindFirst(ClaimTypes.Email)?.Value
        ?? "unknown user";

    // Portal customer tokens set "sub" to the PortalCustomer's own Id too
    // (see JwtTokenService.CreatePortalCustomerAccessToken), plus a dedicated
    // "customer_id" claim - read the dedicated claim here so this can't
    // accidentally resolve a staff AppUser id if that ever changes.
    public static Guid GetCustomerId(this ClaimsPrincipal user)
    {
        var customerId = user.FindFirst("customer_id")?.Value;

        return Guid.TryParse(customerId, out var id)
            ? id
            : throw new InvalidOperationException("Request is missing a valid customer id claim.");
    }
}
