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
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
        modelBuilder.Entity<AppUser>()
            .HasQueryFilter(u => u.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Ticket>()
            .HasIndex(t => new { t.TenantId, t.Status });
        modelBuilder.Entity<Ticket>()
            .HasQueryFilter(t => t.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<SlaPolicy>()
            .HasQueryFilter(s => s.TenantId == _tenantContext.TenantId);

        // Tenants table itself is not filtered - only Super Admin endpoints query it,
        // and they must not go through the tenant-scoped DbContext filter.
    }
}
