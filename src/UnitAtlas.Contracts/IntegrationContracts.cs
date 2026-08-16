using System.Text.Json;

namespace UnitAtlas.Contracts;

public sealed record WebhookEnvelope(
    string Version,
    Guid MessageId,
    Guid CorrelationId,
    string Source,
    string Type,
    DateTimeOffset OccurredAt,
    WebhookSubject Subject,
    JsonElement Data);

public sealed record WebhookSubject(string Type, string Id);
