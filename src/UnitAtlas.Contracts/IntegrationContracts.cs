using System.Text.Json;

namespace UnitAtlas.Contracts;

public sealed record WebhookEnvelope(
    string Version,
    Guid MessageId,
    string Type,
    DateTimeOffset OccurredAt,
    JsonElement Data);
