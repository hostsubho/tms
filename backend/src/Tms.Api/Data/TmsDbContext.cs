using Microsoft.EntityFrameworkCore;
using Tms.Api.Models;

namespace Tms.Api.Data;

// Single Neon Postgres database "TMS" shared across all tenants.
// Every tenant-scoped table gets a global query filter on TenantId so
// application code can never accidentally cross tenant boundaries.
// Postgres Row-Level Security policies provide a second layer of defense
// (see /docs/rls-policies.sql) in case this filter is ever bypassed.
public class TmsDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public TmsDbContext(DbContextOptions<TmsDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PortalCustomer> PortalCustomers => Set<PortalCustomer>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<AutomationRuleLog> AutomationRuleLogs => Set<AutomationRuleLog>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<KnowledgeArticleVersion> KnowledgeArticleVersions => Set<KnowledgeArticleVersion>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CustomRole> CustomRoles => Set<CustomRole>();
    public DbSet<CustomRolePermission> CustomRolePermissions => Set<CustomRolePermission>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDeliveryLog> WebhookDeliveryLogs => Set<WebhookDeliveryLog>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<BillingCredit> BillingCredits => Set<BillingCredit>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<TicketAsset> TicketAssets => Set<TicketAsset>();
    public DbSet<ImpersonationLog> ImpersonationLogs => Set<ImpersonationLog>();
    public DbSet<TenantSsoConfig> SsoConfigs => Set<TenantSsoConfig>();
    public DbSet<SsoLoginState> SsoLoginStates => Set<SsoLoginState>();
    public DbSet<TenantModuleFlag> TenantModuleFlags => Set<TenantModuleFlag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
        modelBuilder.Entity<AppUser>()
            .HasQueryFilter(u => u.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<AppUser>()
            .Property(u => u.Role)
            .HasConversion<string>();
        // Module 8 - Notifications. The C# `= true` initializer only applies
        // to objects constructed in code - it has no effect on the SQL
        // column default EF generates, which otherwise falls back to the
        // CLR default (false for bool). Without this, the migration that
        // added this column would default every *existing* row to false,
        // silently opting every current user out. HasDefaultValue makes the
        // SQL-level default match the C# one.
        modelBuilder.Entity<AppUser>()
            .Property(u => u.NotificationsEnabled)
            .HasDefaultValue(true);

        modelBuilder.Entity<Ticket>()
            .HasIndex(t => new { t.TenantId, t.Status });
        modelBuilder.Entity<Ticket>()
            .HasQueryFilter(t => t.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Ticket>()
            .Property(t => t.Status).HasConversion<string>();
        modelBuilder.Entity<Ticket>()
            .Property(t => t.Priority).HasConversion<string>();

        modelBuilder.Entity<TicketComment>()
            .HasIndex(c => new { c.TenantId, c.TicketId });
        modelBuilder.Entity<TicketComment>()
            .HasQueryFilter(c => c.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Category>()
            .HasIndex(c => new { c.TenantId, c.Name });
        modelBuilder.Entity<Category>()
            .HasQueryFilter(c => c.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<SlaPolicy>()
            .HasQueryFilter(s => s.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<SlaPolicy>()
            .Property(s => s.Priority).HasConversion<string>();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(r => r.TokenHash).IsUnique();
        modelBuilder.Entity<RefreshToken>()
            .HasIndex(r => new { r.TenantId, r.UserId });
        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(r => r.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Tenant>()
            .HasIndex(t => t.Subdomain).IsUnique();
        modelBuilder.Entity<Tenant>()
            .Property(t => t.Status).HasConversion<string>();
        // Client customization - theming (see Tenant's own doc comments).
        modelBuilder.Entity<Tenant>()
            .Property(t => t.ThemeMode).HasConversion<string>();
        modelBuilder.Entity<Tenant>()
            .Property(t => t.BorderRadius).HasConversion<string>();
        modelBuilder.Entity<Tenant>()
            .Property(t => t.Density).HasConversion<string>();

        modelBuilder.Entity<PlatformUser>()
            .HasIndex(p => p.Email).IsUnique();
        modelBuilder.Entity<PlatformUser>()
            .Property(p => p.Role).HasConversion<string>();

        modelBuilder.Entity<Plan>()
            .HasIndex(p => p.Name).IsUnique();

        // PortalCustomers is tenant-scoped the same way AppUsers is - unique
        // per (TenantId, Email) so the same person can hold separate accounts
        // across different tenants' portals.
        modelBuilder.Entity<PortalCustomer>()
            .HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
        modelBuilder.Entity<PortalCustomer>()
            .HasQueryFilter(c => c.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<PortalCustomer>()
            .Property(c => c.NotificationsEnabled)
            .HasDefaultValue(true);

        // Module 8 - Notifications. Indexed for the two "my notifications"
        // list queries (staff and portal), each filtering to their own
        // recipient column plus unread state.
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.TenantId, n.RecipientUserId, n.IsRead });
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.TenantId, n.RecipientCustomerId, n.IsRead });
        modelBuilder.Entity<Notification>()
            .HasQueryFilter(n => n.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Notification>()
            .Property(n => n.Type).HasConversion<string>();

        // Module 5 - Workflow Automation & Business Rules. Rules are looked
        // up per (TenantId, Trigger, IsActive) on every matching event, so
        // that's the index; logs are read back per tenant, newest first.
        modelBuilder.Entity<AutomationRule>()
            .HasIndex(r => new { r.TenantId, r.Trigger, r.IsActive });
        modelBuilder.Entity<AutomationRule>()
            .HasQueryFilter(r => r.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<AutomationRule>()
            .Property(r => r.Trigger).HasConversion<string>();
        modelBuilder.Entity<AutomationRule>()
            .Property(r => r.ConditionField).HasConversion<string>();
        modelBuilder.Entity<AutomationRule>()
            .Property(r => r.ActionType).HasConversion<string>();

        modelBuilder.Entity<AutomationRuleLog>()
            .HasIndex(l => new { l.TenantId, l.FiredAt });
        modelBuilder.Entity<AutomationRuleLog>()
            .HasQueryFilter(l => l.TenantId == _tenantContext.TenantId);

        // Module 6 - Knowledge Base. Indexed for the portal-facing "public
        // articles for this tenant" scan (the search/suggest endpoint loads
        // all of these into memory - see KnowledgeSuggestionMatcher) and for
        // the staff article list.
        modelBuilder.Entity<KnowledgeArticle>()
            .HasIndex(a => new { a.TenantId, a.IsPublic });
        modelBuilder.Entity<KnowledgeArticle>()
            .HasQueryFilter(a => a.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<KnowledgeArticleVersion>()
            .HasIndex(v => new { v.ArticleId, v.EditedAt });
        modelBuilder.Entity<KnowledgeArticleVersion>()
            .HasQueryFilter(v => v.TenantId == _tenantContext.TenantId);

        // Module 5.4 - Security & Compliance / Audit Logging. Read back per
        // tenant, newest first, same shape as AutomationRuleLog's index -
        // this is the only access pattern AuditLogsController uses.
        modelBuilder.Entity<AuditLog>()
            .HasIndex(l => new { l.TenantId, l.Timestamp });
        modelBuilder.Entity<AuditLog>()
            .HasQueryFilter(l => l.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<AuditLog>()
            .Property(l => l.Action).HasConversion<string>();
        modelBuilder.Entity<AuditLog>()
            .Property(l => l.EntityType).HasConversion<string>();

        // Module 12 - Roles & Permissions. Role names are read back per
        // tenant sorted by name (small lists, no pagination needed);
        // permissions are looked up per-role both when resolving a user's
        // JWT claims at login and when rendering a role's checkbox state
        // in the admin UI.
        modelBuilder.Entity<CustomRole>()
            .HasIndex(r => new { r.TenantId, r.Name });
        modelBuilder.Entity<CustomRole>()
            .HasQueryFilter(r => r.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<CustomRolePermission>()
            .HasIndex(p => p.CustomRoleId);
        modelBuilder.Entity<CustomRolePermission>()
            .HasQueryFilter(p => p.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<CustomRolePermission>()
            .Property(p => p.Permission).HasConversion<string>();

        // Module 11 - Integrations & Public API. KeyHash is looked up by
        // ApiKeyAuthenticationHandler before the tenant is known (via
        // IgnoreQueryFilters(), same pattern RefreshToken/AuthController
        // already use), so it needs its own unique index independent of the
        // TenantId+something composite indexes used elsewhere.
        modelBuilder.Entity<ApiKey>()
            .HasIndex(k => k.KeyHash).IsUnique();
        modelBuilder.Entity<ApiKey>()
            .HasQueryFilter(k => k.TenantId == _tenantContext.TenantId);

        // Subscriptions are looked up per (Event, IsActive) on every ticket
        // create/status-change - see WebhookService.DeliverAsync - same
        // shape as AutomationRule's own (Trigger, IsActive) index.
        modelBuilder.Entity<WebhookSubscription>()
            .HasIndex(w => new { w.TenantId, w.Event, w.IsActive });
        modelBuilder.Entity<WebhookSubscription>()
            .HasQueryFilter(w => w.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<WebhookSubscription>()
            .Property(w => w.Event).HasConversion<string>();

        modelBuilder.Entity<WebhookDeliveryLog>()
            .HasIndex(l => new { l.WebhookSubscriptionId, l.AttemptedAt });
        modelBuilder.Entity<WebhookDeliveryLog>()
            .HasQueryFilter(l => l.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<WebhookDeliveryLog>()
            .Property(l => l.Event).HasConversion<string>();

        // Module 5.2 - Plans & Billing Administration. StripeInvoiceId is
        // looked up by StripeWebhookController before any tenant context
        // exists (there's no JWT on an inbound Stripe webhook call) - same
        // reasoning as ApiKey.KeyHash's unique index, and that controller
        // uses IgnoreQueryFilters() for the same reason RefreshToken/ApiKey
        // lookups do pre-tenant-context.
        modelBuilder.Entity<Invoice>()
            .HasIndex(i => i.StripeInvoiceId).IsUnique();
        modelBuilder.Entity<Invoice>()
            .HasIndex(i => new { i.TenantId, i.PeriodStart });
        modelBuilder.Entity<Invoice>()
            .HasQueryFilter(i => i.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Invoice>()
            .Property(i => i.Status).HasConversion<string>();

        modelBuilder.Entity<BillingCredit>()
            .HasIndex(c => new { c.TenantId, c.CreatedAt });
        modelBuilder.Entity<BillingCredit>()
            .HasQueryFilter(c => c.TenantId == _tenantContext.TenantId);

        // Module 10 - Asset Management/CMDB. Listed alphabetically by name
        // for the asset registry view; Type/Status filters (query params on
        // GET /api/assets) don't need their own index at today's per-tenant
        // asset volumes.
        modelBuilder.Entity<Asset>()
            .HasIndex(a => new { a.TenantId, a.Name });
        modelBuilder.Entity<Asset>()
            .HasQueryFilter(a => a.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Asset>()
            .Property(a => a.Type).HasConversion<string>();
        modelBuilder.Entity<Asset>()
            .Property(a => a.Status).HasConversion<string>();

        // Looked up both directions - an asset's ticket history (by AssetId)
        // and a ticket's linked assets (by TicketId) - so both get an index;
        // uniqueness on the pair prevents the same ticket being linked to
        // the same asset twice (LinkTicket already checks this explicitly,
        // this is the DB-level backstop).
        modelBuilder.Entity<TicketAsset>()
            .HasIndex(l => new { l.AssetId, l.TicketId }).IsUnique();
        modelBuilder.Entity<TicketAsset>()
            .HasIndex(l => l.TicketId);
        modelBuilder.Entity<TicketAsset>()
            .HasQueryFilter(l => l.TenantId == _tenantContext.TenantId);

        // Module 5.1 - Tenant impersonation. Platform-scoped, like
        // PlatformUser - no TenantId query filter (only
        // SuperAdminTenantsController ever touches this table, never
        // through the tenant-scoped DbContext filter path). Indexed by
        // StartedAt for the "most recent first" platform-wide list.
        modelBuilder.Entity<ImpersonationLog>()
            .HasIndex(l => l.StartedAt);

        // Module 1 - Authentication & Identity (SSO). One config row per
        // tenant - looked up after SsoConfigController/SsoAuthController.Start
        // resolve the tenant (by JWT claim or by tenantSlug respectively),
        // same as every other tenant-scoped table here.
        modelBuilder.Entity<TenantSsoConfig>()
            .HasIndex(c => c.TenantId).IsUnique();
        modelBuilder.Entity<TenantSsoConfig>()
            .HasQueryFilter(c => c.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<TenantSsoConfig>()
            .Property(c => c.Protocol).HasConversion<string>();

        // The callback/ACS endpoint looks this up by TokenHash before the
        // tenant is known (that's the whole point of the state token) - same
        // pre-tenant-context IgnoreQueryFilters() pattern as RefreshToken/
        // ApiKey, so it needs its own unique index independent of TenantId.
        modelBuilder.Entity<SsoLoginState>()
            .HasIndex(s => s.TokenHash).IsUnique();
        modelBuilder.Entity<SsoLoginState>()
            .HasQueryFilter(s => s.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<SsoLoginState>()
            .Property(s => s.Protocol).HasConversion<string>();

        // "Module Licensing" - see TenantModuleFlag's own doc comment for why
        // this has no HasQueryFilter (same as Tenant/PlatformUser).
        modelBuilder.Entity<TenantModuleFlag>()
            .HasIndex(f => new { f.TenantId, f.ModuleKey }).IsUnique();
        modelBuilder.Entity<TenantModuleFlag>()
            .Property(f => f.ModuleKey).HasConversion<string>();

        // Tenants table itself is not filtered - only Super Admin endpoints query it,
        // and they must not go through the tenant-scoped DbContext filter.
        // Auth endpoints that need to resolve a tenant before TenantId is known
        // (e.g. login by subdomain) use IgnoreQueryFilters() explicitly - see AuthController.
        // PlatformUsers is likewise unfiltered - it has no TenantId at all, by design
        // (platform staff are never members of a tenant).
    }
}
