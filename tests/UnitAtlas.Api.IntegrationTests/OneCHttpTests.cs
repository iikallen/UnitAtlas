using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class OneCHttpTests
{
    private readonly HttpClient client = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("UNITATLAS_TEST_URL") ?? "http://host.docker.internal:8080")
    };

    [Fact]
    public async Task Reference_flow_is_idempotent_and_delivers_outbound_events()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var system = $"ONEC_{suffix[..8]}".ToUpperInvariant();
        var endpointResponse = await client.PostAsJsonAsync("/api/v1/integration-endpoints", new
        {
            system,
            adapter = "ONE_C",
            baseAddress = $"http://api:8080/api/v1/integration-inbox/{system}",
            settings = new { eventTypes = new[] { "unit.created", "trace_event.recorded", "aggregation.recorded" } },
            enabled = true
        });
        Assert.Equal(HttpStatusCode.Created, endpointResponse.StatusCode);
        var endpointId = (await endpointResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var productExternalId = $"product-{suffix}";
        var productMessageId = $"message-product-{suffix}";
        var product = new
        {
            type = "product.upsert",
            data = new { externalId = productExternalId, sku = $"SKU-{suffix[..10]}", name = "1C imported product", gtin = $"95{Random.Shared.NextInt64(0, 1_000_000_000_000):D12}" }
        };
        Assert.Equal(HttpStatusCode.Created, (await Send(system, productMessageId, product)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(system, productMessageId, product)).StatusCode);
        var conflict = await Send(system, productMessageId, new { type = "product.upsert", data = new { externalId = productExternalId, sku = "changed", name = "changed", gtin = "95000000000001" } });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("INBOX_IDEMPOTENCY_CONFLICT", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var unitExternalId = $"unit-{suffix}";
        var production = await Send(system, $"message-production-{suffix}", new
        {
            type = "production.completed",
            data = new { externalId = unitExternalId, productExternalId, serial = $"SER-{suffix}", lot = $"LOT-{suffix[..8]}", occurredAt = "2026-08-16T10:00:00Z" }
        });
        Assert.Equal(HttpStatusCode.Created, production.StatusCode);
        var atlasId = (await production.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("atlasId").GetString()!;

        Assert.Equal(HttpStatusCode.Created, (await Send(system, $"message-shipment-{suffix}", new
        {
            type = "shipment.recorded",
            data = new { externalId = $"shipment-{suffix}", unitExternalId, location = "Outbound dock", occurredAt = "2026-08-16T11:00:00Z" }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(system, $"message-receipt-{suffix}", new
        {
            type = "receipt.recorded",
            data = new { externalId = $"receipt-{suffix}", unitExternalId, location = "Customer warehouse", occurredAt = "2026-08-16T12:00:00Z" }
        })).StatusCode);

        var events = await client.GetFromJsonAsync<JsonElement>($"/api/v1/units/{Uri.EscapeDataString(atlasId)}/events");
        Assert.Contains(events.EnumerateArray(), x => x.GetProperty("eventType").GetString() == "SHIPPED" && x.GetProperty("sourceSystem").GetString() == system);
        Assert.Contains(events.EnumerateArray(), x => x.GetProperty("eventType").GetString() == "RECEIVED" && x.GetProperty("sourceSystem").GetString() == system);

        JsonElement delivery = default;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var deliveries = await client.GetFromJsonAsync<JsonElement>($"/api/v1/integration-endpoints/{endpointId}/deliveries");
            if (deliveries.EnumerateArray().FirstOrDefault(x => x.GetProperty("status").GetString() == "Delivered") is var found
                && found.ValueKind != JsonValueKind.Undefined)
            {
                delivery = found;
                break;
            }
            await Task.Delay(250);
        }
        Assert.Equal("Delivered", delivery.GetProperty("status").GetString());
    }

    private Task<HttpResponseMessage> Send(string system, string messageId, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/integration-inbox/{system}/1c")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-External-Message-Id", messageId);
        return client.SendAsync(request);
    }
}
