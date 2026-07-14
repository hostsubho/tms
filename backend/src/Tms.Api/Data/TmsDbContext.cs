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

        // Tenants table itself is not filtered - only Super Admin endpoints query it,
        // and they must not go through the tenant-scoped DbContext filter.
        // Auth endpoints that need to resolve a tenant before TenantId is known
        // (e.g. login by subdomain) use IgnoreQueryFilters() explicitly - see AuthController.
        // PlatformUsers is likewise unfiltered - it has no TenantId at all, by design
        // (platform staff are never members of a tenant).
    }
}
