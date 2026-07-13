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
}
