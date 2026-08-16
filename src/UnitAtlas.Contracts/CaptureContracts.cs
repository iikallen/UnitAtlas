namespace UnitAtlas.Contracts;

public sealed record CaptureSyncRequest(
    Guid CommandId,
    string? DeviceId,
    string? CommandType,
    string? ParentCode,
    string? Action,
    IReadOnlyCollection<string>? UnitAtlasIds,
    IReadOnlyCollection<string>? LogisticUnitCodes,
    DateTimeOffset? OccurredAt,
    Guid? ReadPointId,
    Guid? BusinessLocationId);

public sealed record CaptureResolveRequest(string? Code);
