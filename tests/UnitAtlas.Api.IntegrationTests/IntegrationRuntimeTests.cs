using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class IntegrationRuntimeTests
{
    private readonly HttpClient client = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("UNITATLAS_TEST_URL") ?? "http://host.docker.internal:8080")
    };

    [Fact]
    public async Task Outbox_fans_out_and_inbox_is_idempotent()
    {
        var system = $"SINK_{Guid.NewGuid():N}"[..13].ToUpperInvariant();
        var create = await client.PostAsJsonAsync("/api/v1/integration-endpoints", new
        {
            system,
            adapter = "WEBHOOK",
            baseAddress = $"http://api:8080/api/v1/integration-inbox/{system}",
            settings = new { },
            enabled = true
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var endpointId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var unit = await client.PostAsJsonAsync("/api/v1/logistic-units", new
        {
            code = $"BOX-RUNTIME-{Guid.NewGuid():N}",
            type = "BOX"
        });
        Assert.Equal(HttpStatusCode.Created, unit.StatusCode);

        JsonElement delivery = default;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var deliveries = await client.GetFromJsonAsync<JsonElement>($"/api/v1/integration-endpoints/{endpointId}/deliveries");
            if (deliveries.GetArrayLength() > 0 && deliveries[0].GetProperty("status").GetString() == "Delivered")
            {
                delivery = deliveries[0];
                break;
            }
            await Task.Delay(250);
        }
        Assert.Equal("Delivered", delivery.GetProperty("status").GetString());
        Assert.Equal(1, delivery.GetProperty("attemptCount").GetInt32());

        var externalId = $"external-{Guid.NewGuid():N}";
        using var first = InboxRequest(system, externalId, new { reference = "A" });
        using var replay = InboxRequest(system, externalId, new { reference = "A" });
        using var conflict = InboxRequest(system, externalId, new { reference = "B" });
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(replay)).StatusCode);
        var conflictResponse = await client.SendAsync(conflict);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal("INBOX_IDEMPOTENCY_CONFLICT",
            (await conflictResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private static HttpRequestMessage InboxRequest(string system, string externalId, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/integration-inbox/{system}")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-External-Message-Id", externalId);
        return request;
    }
}
