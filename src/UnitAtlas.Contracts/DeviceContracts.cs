using System.Text.Json;

namespace UnitAtlas.Contracts;

public sealed record DeviceRequest(string? Code, string? Name, string? Platform);

public sealed record StationRequest(
    string? Code,
    string? Name,
    Guid SiteId,
    Guid ReadPointId,
    Guid BusinessLocationId);

public sealed record DeviceEnrollmentRequest(Guid DeviceId, Guid StationId, DateTimeOffset? ExpiresAt);

public sealed record CaptureEnrollRequest(string? DeviceCode, string? EnrollmentCode);

public sealed record CaptureChangeResponse(
    long Token,
    string ResourceType,
    string ResourceId,
    string Action,
    JsonElement Payload);

public sealed record CaptureChangesResponse(
    IReadOnlyCollection<CaptureChangeResponse> Changes,
    string NextToken,
    bool HasMore);
