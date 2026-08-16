using System.Text.Json;
using Microsoft.Extensions.Configuration;
using UnitAtlas.Application.Integrations;
using UnitAtlas.Contracts;

namespace UnitAtlas.Infrastructure.Integrations;

public sealed class OneCReferenceAdapter(HttpClient client, IConfiguration configuration) : IIntegrationAdapter
{
    public string Name => "ONE_C";

    public async Task<IntegrationSendResult> SendAsync(
        IntegrationTarget target, WebhookEnvelope message, CancellationToken cancellationToken)
    {
        var eventType = message.Type switch
        {
            "unit.created" => "unit.created",
            "trace_event.recorded" => "trace.recorded",
            "aggregation.recorded" when Action(message.Data) == "ADD" => "aggregation.added",
            "aggregation.recorded" when Action(message.Data) == "DELETE" => "aggregation.deleted",
            _ => null
        };
        if (eventType is null) return new(false, false, "ONE_C_EVENT_NOT_SUPPORTED");

        var payload = new
        {
            schemaVersion = "1.0",
            messageId = message.MessageId,
            correlationId = message.CorrelationId,
            occurredAt = message.OccurredAt,
            eventType,
            subject = message.Subject,
            data = message.Data
        };
        return await HttpIntegrationTransport.SendAsync(
            client, configuration, target, message.MessageId, message.CorrelationId, payload, cancellationToken);
    }

    private static string? Action(JsonElement data) =>
        data.TryGetProperty("action", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.ToUpperInvariant()
            : null;
}
