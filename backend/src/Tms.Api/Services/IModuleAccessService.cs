using Tms.Api.Models;

namespace Tms.Api.Services;

// "Module Licensing." A single place every module-gated controller asks
// "is this on for this tenant" - replaces the one-off Tenant.CmdbEnabled
// check AssetsController used to do inline, generalized to every optional
// module (see ModuleKey). Kept as a thin interface (not a static helper)
// so it can be mocked/swapped in tests, matching every other service in
// this codebase.
public interface IModuleAccessService
{
    Task<bool> IsEnabledAsync(Guid tenantId, ModuleKey key, CancellationToken ct);
}
