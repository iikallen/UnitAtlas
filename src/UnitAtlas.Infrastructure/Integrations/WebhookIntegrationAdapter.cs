using Microsoft.Extensions.Configuration;
using UnitAtlas.Application.Integrations;
using UnitAtlas.Contracts;

namespace UnitAtlas.Infrastructure.Integrations;

public sealed class WebhookIntegrationAdapter(HttpClient client, IConfiguration configuration) : IIntegrationAdapter
{
    public string Name => "WEBHOOK";

    public async Task<IntegrationSendResult> SendAsync(
        IntegrationTarget target,
        WebhookEnvelope message,
        CancellationToken cancellationToken)
    {
        return await HttpIntegrationTransport.SendAsync(
            client, configuration, target, message.MessageId, message.CorrelationId, message, cancellationToken);
    }
}
