using Microsoft.EntityFrameworkCore;
using UnitAtlas.Domain;

namespace UnitAtlas.Infrastructure.Persistence;

public sealed class UnitAtlasDb(DbContextOptions<UnitAtlasDb> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<TrackedUnit> Units => Set<TrackedUnit>();
    public DbSet<TraceEvent> TraceEvents => Set<TraceEvent>();
    public DbSet<UnitState> UnitStates => Set<UnitState>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Tenant>().ToTable("tenants");

        model.Entity<Product>().ToTable("products");
        model.Entity<Product>().HasAlternateKey(x => new { x.TenantId, x.Id });
        model.Entity<Product>().HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        model.Entity<Product>().HasIndex(x => new { x.TenantId, x.Gtin }).IsUnique();
        model.Entity<Product>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

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

        model.Entity<TraceEvent>().ToTable("trace_events");
        model.Entity<TraceEvent>().HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        model.Entity<TraceEvent>().HasIndex(x => new { x.TenantId, x.UnitId, x.Sequence }).IsUnique();
        model.Entity<TraceEvent>().HasIndex(x => new { x.TenantId, x.UnitId, x.OccurredAt, x.Sequence });
        model.Entity<TraceEvent>().HasOne<TrackedUnit>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });

        model.Entity<UnitState>().ToTable("unit_states").HasKey(x => x.UnitId);
        model.Entity<UnitState>().HasOne<TrackedUnit>().WithOne()
            .HasForeignKey<UnitState>(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey<TrackedUnit>(x => new { x.TenantId, x.Id });
    }
}
