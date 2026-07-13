using Tms.Api.Data;

namespace Tms.Api.Middleware;

// Resolves the current tenant from the "X-Tenant-Slug" header (dev/API clients)
// or from the authenticated user's "tenant_id" JWT claim (browser sessions),
// and sets it on the scoped ITenantContext before any DbContext query runs.
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var claim = context.User?.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(claim, out var tenantIdFromClaim))
        {
            tenantContext.TenantId = tenantIdFromClaim;
        }
        else if (context.Request.Headers.TryGetValue("X-Tenant-Slug", out var slugHeader))
        {
            // In a full implementation this looks up Tenants by Subdomain via a
            // lightweight, unfiltered query and caches the result (Redis) per request.
            // Left as a TODO hook for Module 2 (Tenant Onboarding) implementation.
        }

        await _next(context);
    }
}
