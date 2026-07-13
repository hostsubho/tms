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

        // Tenants table itself is not filtered - only Super Admin endpoints query it,
        // and they must not go through the tenant-scoped DbContext filter.
        // Auth endpoints that need to resolve a tenant before TenantId is known
        // (e.g. login by subdomain) use IgnoreQueryFilters() explicitly - see AuthController.
    }
}
