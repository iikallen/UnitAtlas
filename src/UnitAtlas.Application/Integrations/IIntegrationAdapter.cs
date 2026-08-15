using UnitAtlas.Contracts;

namespace UnitAtlas.Application.Integrations;

public interface IIntegrationAdapter
{
    string System { get; }
    Task SendAsync(WebhookEnvelope message, CancellationToken cancellationToken);
}
