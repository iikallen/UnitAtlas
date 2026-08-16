using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using UnitAtlas.Application.Integrations;
using UnitAtlas.Contracts;
using UnitAtlas.Infrastructure.Integrations;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class OneCReferenceAdapterTests
{
    [Theory]
    [InlineData("ADD", "aggregation.added")]
    [InlineData("DELETE", "aggregation.deleted")]
    public async Task Aggregation_actions_are_mapped_to_the_reference_contract(string action, string expected)
    {
        var handler = new RecordingHandler();
        var adapter = new OneCReferenceAdapter(new HttpClient(handler), new ConfigurationBuilder().Build());
        var messageId = Guid.NewGuid();
        var envelope = new WebhookEnvelope(
            "1.0", messageId, Guid.NewGuid(), "unitatlas", "aggregation.recorded", DateTimeOffset.UtcNow,
            new("AggregationEvent", Guid.NewGuid().ToString()), JsonSerializer.SerializeToElement(new { action }));

        var result = await adapter.SendAsync(
            new("ONE_C", "https://example.test/1c", null, "{}"), envelope, CancellationToken.None);

        Assert.True(result.Delivered);
        Assert.Equal(messageId.ToString(), handler.Request!.Headers.GetValues("X-External-Message-Id").Single());
        Assert.Equal(expected, handler.Body.GetProperty("eventType").GetString());
        Assert.Equal("1.0", handler.Body.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public async Task Unknown_events_are_dead_lettered_without_an_http_call()
    {
        var handler = new RecordingHandler();
        var adapter = new OneCReferenceAdapter(new HttpClient(handler), new ConfigurationBuilder().Build());
        var envelope = new WebhookEnvelope(
            "1.0", Guid.NewGuid(), Guid.NewGuid(), "unitatlas", "unknown", DateTimeOffset.UtcNow,
            new("Unknown", "1"), JsonSerializer.SerializeToElement(new { }));

        var result = await adapter.SendAsync(
            new("ONE_C", "https://example.test/1c", null, "{}"), envelope, CancellationToken.None);

        Assert.Equal("ONE_C_EVENT_NOT_SUPPORTED", result.ErrorCode);
        Assert.Null(handler.Request);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public JsonElement Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(HttpStatusCode.OK);
        }
    }
}
