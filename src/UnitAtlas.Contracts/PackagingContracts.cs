namespace UnitAtlas.Contracts;

public sealed record LogisticUnitRequest(string? Code, string? Type, string? Sscc);

public sealed record AggregationRequest(
    string? Action,
    string? IdempotencyKey,
    IReadOnlyCollection<string>? UnitAtlasIds,
    IReadOnlyCollection<string>? LogisticUnitCodes,
    DateTimeOffset? OccurredAt,
    Guid? ReadPointId,
    Guid? BusinessLocationId,
    string? SourceSystem);

public sealed record LogisticUnitContentResponse(
    string Code,
    string Type,
    string? Sscc,
    IReadOnlyCollection<LogisticUnitChildResponse> Children,
    IReadOnlyCollection<AggregationEventResponse> Events);

public sealed record LogisticUnitChildResponse(
    string Kind,
    string Code,
    string? Product,
    string? Serial);

public sealed record AggregationEventResponse(
    Guid Id,
    string Action,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    long Sequence,
    string ActorSubject,
    string SourceSystem,
    Guid? ReadPointId,
    Guid? BusinessLocationId,
    IReadOnlyCollection<string> UnitAtlasIds,
    IReadOnlyCollection<string> LogisticUnitCodes);
