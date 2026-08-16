using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        using var request = new HttpRequestMessage(HttpMethod.Post, target.BaseAddress) { Content = JsonContent.Create(message) };
        request.Headers.Add("Idempotency-Key", message.MessageId.ToString());
        request.Headers.Add("X-External-Message-Id", message.MessageId.ToString());
        request.Headers.Add("X-Correlation-Id", message.CorrelationId.ToString());

        if (target.SecretRef is not null)
        {
            var secret = configuration[$"IntegrationSecrets:{target.SecretRef}"];
            if (string.IsNullOrWhiteSpace(secret)) return new(false, false, "SECRET_NOT_CONFIGURED");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return new(true, false);

            var retryable = response.StatusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;
            return new(false, retryable, $"HTTP_{(int)response.StatusCode}", RetryAt(response));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, true, "TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return new(false, true, "NETWORK_ERROR");
        }
    }

    private static DateTimeOffset? RetryAt(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Date is { } date) return date;
        if (retry?.Delta is { } delta) return DateTimeOffset.UtcNow.Add(delta);
        return null;
    }
}
