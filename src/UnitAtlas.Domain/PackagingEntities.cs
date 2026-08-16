namespace UnitAtlas.Domain;

public sealed class LogisticUnit
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Type { get; set; }
    public string? Sscc { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AggregationEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ParentLogisticUnitId { get; set; }
    public required string Action { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long Sequence { get; set; }
    public required string ActorSubject { get; set; }
    public required string SourceSystem { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string ChildrenJson { get; set; }
    public Guid? ReadPointId { get; set; }
    public Guid? BusinessLocationId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? StationId { get; set; }
    public Guid? CorrelationId { get; set; }
}

public sealed class LogisticUnitContent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ParentLogisticUnitId { get; set; }
    public Guid? ChildUnitId { get; set; }
    public Guid? ChildLogisticUnitId { get; set; }
    public Guid AddedByEventId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
