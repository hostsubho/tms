namespace Tms.Api.Models;

// "Module Licensing" - Owner-controlled per-tenant feature flags with an
// optional negotiated monthly price, generalizing what Module 10 started
// with the single Tenant.CmdbEnabled bit into one system covering every
// optional module. Every module here is enabled by default (no row for a
// given (TenantId, ModuleKey) pair means enabled) EXCEPT Cmdb, which keeps
// falling back to Tenant.CmdbEnabled (false unless a Super Admin already
// turned it on via the older toggle) - see IModuleAccessService for the
// exact fallback logic. This means every tenant that predates this feature
// keeps working exactly as before with zero data migration: nothing gets
// silently switched off, and Cmdb's existing off-by-default behavior is
// preserved bit for bit.
public enum ModuleKey
{
    SlaPolicies,
    Automation,
    KnowledgeBase,
    AdvancedReports,
    Cmdb,
    IntegrationsApi,
    CustomRoles,
    Sso,
}

// Deliberately NOT tenant-query-filtered (no HasQueryFilter in TmsDbContext,
// same as Tenant/PlatformUser) - both a platform-scoped Super Admin request
// (no ambient tenant) and a tenant-scoped request (via IModuleAccessService)
// need to read/write this table, so every query here filters by TenantId
// explicitly instead of relying on the ambient ITenantContext, same
// reasoning as Tenant itself.
public class TenantModuleFlag
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public ModuleKey ModuleKey { get; set; }
    public bool Enabled { get; set; }

    // Owner-negotiated monthly price for this module, for this tenant - null
    // means "not priced" (the module still works if Enabled, it just adds
    // nothing to the suggested billing total computed in
    // SuperAdminTenantsController.GetModuleLicensing). Deliberately not
    // wired to an actual Stripe subscription line item - this is a Super
    // Admin cost-tracking/negotiation tool, not automated multi-line
    // invoicing (see Tenant.ModuleBillingTotalOverrideCents for the
    // complementary "override the whole total" lever). Real per-module
    // Stripe billing is future work once actual prices are decided.
    public long? MonthlyCostCents { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
