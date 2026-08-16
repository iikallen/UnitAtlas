namespace UnitAtlas.Domain;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string RegulatoryGatewayMode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum TenantRole
{
    Owner,
    Admin,
    ProductionManager,
    ProductionOperator,
    QualityManager,
    QualityOperator,
    WarehouseManager,
    WarehouseOperator,
    Viewer
}

public sealed class TenantMembership
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string UserSubject { get; set; }
    public TenantRole Role { get; set; }
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
    public Guid? LotId { get; set; }
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
    public string? ActorSubject { get; set; }
    public required string SourceSystem { get; set; }
    public required string IdempotencyKey { get; set; }
    public string? BusinessStep { get; set; }
    public string? Disposition { get; set; }
    public Guid? ReadPointId { get; set; }
    public Guid? BusinessLocationId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? StationId { get; set; }
    public Guid? CorrelationId { get; set; }
    public string? MetadataJson { get; set; }
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
