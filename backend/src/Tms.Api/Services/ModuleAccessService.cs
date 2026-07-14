using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Models;

namespace Tms.Api.Services;

public class ModuleAccessService : IModuleAccessService
{
    private readonly TmsDbContext _db;

    public ModuleAccessService(TmsDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsEnabledAsync(Guid tenantId, ModuleKey key, CancellationToken ct)
    {
        // TenantModuleFlags has no query filter (see the model's own doc
        // comment) - filtered explicitly by the tenantId the caller passed
        // in, same as every other cross-cutting-concern lookup in this app
        // (RefreshToken/ApiKey by hash, Tenant by Id).
        var flag = await _db.TenantModuleFlags
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.ModuleKey == key, ct);

        if (flag is not null) return flag.Enabled;

        // No explicit row yet - every module defaults to enabled EXCEPT
        // Cmdb, which keeps deferring to the older Tenant.CmdbEnabled bit
        // (false unless a Super Admin already turned it on via the Module 10
        // toggle) so this feature's rollout doesn't silently change what any
        // existing tenant can already do. The first time an Owner touches
        // Cmdb through the new module-licensing endpoint, a real row is
        // written and this fallback stops applying to it (see
        // SuperAdminTenantsController.UpdateModuleFlag).
        if (key == ModuleKey.Cmdb)
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            return tenant?.CmdbEnabled ?? false;
        }

        return true;
    }
}
