using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using UnitAtlas.Application.Integrations;

namespace UnitAtlas.Infrastructure.Integrations;

internal static class HttpIntegrationTransport
{
    public static async Task<IntegrationSendResult> SendAsync(
        HttpClient client,
        IConfiguration configuration,
        IntegrationTarget target,
        Guid messageId,
        Guid correlationId,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, target.BaseAddress) { Content = JsonContent.Create(payload) };
        request.Headers.Add("Idempotency-Key", messageId.ToString());
        request.Headers.Add("X-External-Message-Id", messageId.ToString());
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
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
            var retryable = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            var retry = response.Headers.RetryAfter;
            var retryAt = retry?.Date ?? (retry?.Delta is { } delta ? DateTimeOffset.UtcNow.Add(delta) : null);
            return new(false, retryable, $"HTTP_{(int)response.StatusCode}", retryAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, true, "TIMEOUT"); }
        catch (HttpRequestException) { return new(false, true, "NETWORK_ERROR"); }
    }
}
