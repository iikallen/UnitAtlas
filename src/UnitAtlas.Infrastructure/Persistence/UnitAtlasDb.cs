using Microsoft.EntityFrameworkCore;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Domain;

namespace UnitAtlas.Infrastructure.Persistence;

public sealed class UnitAtlasDb(DbContextOptions<UnitAtlasDb> options, ITenantContext tenantContext) : DbContext(options)
{
    private Guid CurrentTenantId => tenantContext.IsAvailable ? tenantContext.TenantId : Guid.Empty;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<TrackedUnit> Units => Set<TrackedUnit>();
    public DbSet<TraceEvent> TraceEvents => Set<TraceEvent>();
    public DbSet<UnitState> UnitStates => Set<UnitState>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Tenant>().ToTable("tenants");

        model.Entity<TenantMembership>().ToTable("tenant_memberships");
        model.Entity<TenantMembership>().Property(x => x.Role).HasConversion<string>();
        model.Entity<TenantMembership>().HasIndex(x => new { x.TenantId, x.UserSubject }).IsUnique();
        model.Entity<TenantMembership>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        model.Entity<TenantMembership>().HasQueryFilter(x => x.TenantId == CurrentTenantId);

        model.Entity<Product>().ToTable("products");
        model.Entity<Product>().HasAlternateKey(x => new { x.TenantId, x.Id });
        model.Entity<Product>().HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        model.Entity<Product>().HasIndex(x => new { x.TenantId, x.Gtin }).IsUnique();
        model.Entity<Product>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<Product>().HasQueryFilter(x => x.TenantId == CurrentTenantId);

        model.Entity<TrackedUnit>().ToTable("units");
        model.Entity<TrackedUnit>().HasAlternateKey(x => new { x.TenantId, x.Id });
        model.Entity<TrackedUnit>().HasIndex(x => new { x.TenantId, x.AtlasId }).IsUnique();
        model.Entity<TrackedUnit>().HasIndex(x => new { x.TenantId, x.Serial }).IsUnique();
        model.Entity<TrackedUnit>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<TrackedUnit>().HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });
        model.Entity<TrackedUnit>().HasQueryFilter(x => x.TenantId == CurrentTenantId);

        model.Entity<TraceEvent>().ToTable("trace_events");
        model.Entity<TraceEvent>().HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        model.Entity<TraceEvent>().HasIndex(x => new { x.TenantId, x.UnitId, x.Sequence }).IsUnique();
        model.Entity<TraceEvent>().HasIndex(x => new { x.TenantId, x.UnitId, x.OccurredAt, x.Sequence });
        model.Entity<TraceEvent>().HasOne<TrackedUnit>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });
        model.Entity<TraceEvent>().HasQueryFilter(x => x.TenantId == CurrentTenantId);

        model.Entity<UnitState>().ToTable("unit_states").HasKey(x => x.UnitId);
        model.Entity<UnitState>().HasOne<TrackedUnit>().WithOne()
            .HasForeignKey<UnitState>(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey<TrackedUnit>(x => new { x.TenantId, x.Id });
        model.Entity<UnitState>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
    }
}
