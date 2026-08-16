using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using UnitAtlas.Application.Integrations;
using UnitAtlas.Contracts;
using UnitAtlas.Infrastructure.Integrations;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class WebhookIntegrationAdapterTests
{
    [Fact]
    public async Task Retry_after_is_honored_for_429()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30));
        var adapter = Adapter(_ => response);

        var result = await adapter.SendAsync(Target(), Envelope(), CancellationToken.None);

        Assert.False(result.Delivered);
        Assert.True(result.Retryable);
        Assert.Equal("HTTP_429", result.ErrorCode);
        Assert.True(result.RetryAt > DateTimeOffset.UtcNow.AddSeconds(20));
    }

    [Fact]
    public async Task Client_error_is_not_retried()
    {
        var adapter = Adapter(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        var result = await adapter.SendAsync(Target(), Envelope(), CancellationToken.None);

        Assert.False(result.Delivered);
        Assert.False(result.Retryable);
        Assert.Equal("HTTP_400", result.ErrorCode);
    }

    [Fact]
    public async Task Server_error_is_retried()
    {
        var adapter = Adapter(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await adapter.SendAsync(Target(), Envelope(), CancellationToken.None);

        Assert.False(result.Delivered);
        Assert.True(result.Retryable);
        Assert.Equal("HTTP_500", result.ErrorCode);
    }

    [Fact]
    public async Task Timeout_is_retried()
    {
        var adapter = new WebhookIntegrationAdapter(
            new HttpClient(new ThrowingHandler()), new ConfigurationBuilder().Build());

        var result = await adapter.SendAsync(Target(), Envelope(), CancellationToken.None);

        Assert.False(result.Delivered);
        Assert.True(result.Retryable);
        Assert.Equal("TIMEOUT", result.ErrorCode);
    }

    [Fact]
    public async Task Missing_referenced_secret_fails_without_sending()
    {
        var sent = false;
        var adapter = Adapter(_ => { sent = true; return new HttpResponseMessage(HttpStatusCode.OK); });

        var result = await adapter.SendAsync(Target("missing"), Envelope(), CancellationToken.None);

        Assert.Equal("SECRET_NOT_CONFIGURED", result.ErrorCode);
        Assert.False(sent);
    }

    private static WebhookIntegrationAdapter Adapter(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new HttpClient(new StubHandler(response)), new ConfigurationBuilder().Build());

    private static IntegrationTarget Target(string? secretRef = null) =>
        new("WEBHOOK", "https://example.test/events", secretRef, "{}");

    private static WebhookEnvelope Envelope() => new(
        "1.0", Guid.NewGuid(), Guid.NewGuid(), "unitatlas", "unit.created", DateTimeOffset.UtcNow,
        new("Unit", "UA-1"), JsonSerializer.SerializeToElement(new { id = "UA-1" }));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException();
    }
}
