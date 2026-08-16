namespace UnitAtlas.Contracts;

public sealed record CaptureSyncRequest(
    Guid CommandId,
    string? DeviceId,
    string? CommandType,
    string? ParentCode,
    string? Action,
    IReadOnlyCollection<string>? UnitAtlasIds,
    IReadOnlyCollection<string>? LogisticUnitCodes,
    string? UnitAtlasId,
    string? EventType,
    string? Location,
    DateTimeOffset? OccurredAt,
    Guid? ReadPointId,
    Guid? BusinessLocationId);

public sealed record CaptureResolveRequest(string? Code);

public sealed record CaptureProductionRequest(
    Guid CommandId,
    string? DeviceId,
    string? ScannedCode,
    string? Location,
    DateTimeOffset? OccurredAt);

public sealed record CaptureQualityRequest(
    Guid CommandId,
    string? DeviceId,
    string? UnitAtlasId,
    string? Outcome,
    string? Location,
    DateTimeOffset? OccurredAt,
    Guid? ReadPointId,
    Guid? BusinessLocationId);

public sealed record CaptureMoveRequest(
    Guid CommandId,
    string? DeviceId,
    string? UnitAtlasId,
    string? To,
    DateTimeOffset? OccurredAt,
    Guid? ReadPointId,
    Guid? BusinessLocationId);
