namespace UnitAtlas.Contracts;

public sealed record ProductRequest(string? Sku, string? Name, string? Gtin);
public sealed record UnitRequest(Guid ProductId, string? Serial, string? Lot, DateTimeOffset? ManufacturedAt);
public sealed record EventRequest(
    string? EventType,
    string? Location,
    string? IdempotencyKey,
    string? Actor,
    DateTimeOffset? OccurredAt,
    Guid? ReadPointId,
    Guid? BusinessLocationId,
    string? BusinessStep,
    string? Disposition,
    string? SourceSystem);
public sealed record UnitSummary(string AtlasId, string Serial, string Lot, string Product, string Sku, string Gtin, string Status, string Location, DateTimeOffset UpdatedAt);
public sealed record TraceEventResponse(
    Guid Id,
    string EventType,
    string Location,
    string Actor,
    string? ActorSubject,
    string SourceSystem,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    long Sequence,
    Guid? ReadPointId,
    Guid? BusinessLocationId,
    string? BusinessStep,
    string? Disposition);
