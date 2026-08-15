namespace UnitAtlas.Domain;

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
    public long Sequence { get; set; }
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
    public DateTimeOffset CurrentOccurredAt { get; set; }
    public long CurrentSequence { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
