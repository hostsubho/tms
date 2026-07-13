namespace Tms.Api.Data;

// Resolved per-request from JWT claim or subdomain by TenantResolutionMiddleware.
public interface ITenantContext
{
    Guid? TenantId { get; set; }
}

public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
}
