using Microsoft.EntityFrameworkCore;

namespace UnitAtlas.Api;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Product
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public required string Gtin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TrackedUnit
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public required string AtlasId { get; set; }
    public required string Serial { get; set; }
    public required string Lot { get; set; }
    public DateTimeOffset ManufacturedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TraceEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UnitId { get; set; }
    public required string EventType { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public required string Location { get; set; }
    public required string Actor { get; set; }
    public required string SourceSystem { get; set; }
    public required string IdempotencyKey { get; set; }
}

public sealed class UnitState
{
    public Guid UnitId { get; set; }
    public Guid TenantId { get; set; }
    public required string Status { get; set; }
    public required string Location { get; set; }
    public Guid LastEventId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

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
        model.Entity<Product>().ToTable("products").HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        model.Entity<Product>().HasIndex(x => new { x.TenantId, x.Gtin }).IsUnique();
        model.Entity<TrackedUnit>().ToTable("units").HasIndex(x => new { x.TenantId, x.AtlasId }).IsUnique();
        model.Entity<TrackedUnit>().HasIndex(x => new { x.TenantId, x.Serial }).IsUnique();
        model.Entity<TrackedUnit>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        model.Entity<TraceEvent>().ToTable("trace_events").HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        model.Entity<TraceEvent>().HasIndex(x => new { x.UnitId, x.OccurredAt });
        model.Entity<UnitState>().ToTable("unit_states").HasKey(x => x.UnitId);
    }
}

public sealed record ProductRequest(string Sku, string Name, string Gtin);
public sealed record UnitRequest(Guid ProductId, string Serial, string Lot, DateTimeOffset? ManufacturedAt);
public sealed record EventRequest(string EventType, string Location, string IdempotencyKey, string? Actor, DateTimeOffset? OccurredAt);
public sealed record UnitSummary(string AtlasId, string Serial, string Lot, string Product, string Sku, string Gtin, string Status, string Location, DateTimeOffset UpdatedAt);
