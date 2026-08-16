using UnitAtlas.Contracts;

namespace UnitAtlas.Application.Integrations;

public sealed record IntegrationTarget(string Adapter, string BaseAddress, string? SecretRef, string SettingsJson);

public sealed record IntegrationSendResult(
    bool Delivered,
    bool Retryable,
    string? ErrorCode = null,
    DateTimeOffset? RetryAt = null);

public interface IIntegrationAdapter
{
    string Name { get; }
    Task<IntegrationSendResult> SendAsync(
        IntegrationTarget target,
        WebhookEnvelope message,
        CancellationToken cancellationToken);
}
