using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class IntegrationOperationsTests
{
    private readonly HttpClient client = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("UNITATLAS_TEST_URL") ?? "http://host.docker.internal:8080")
    };

    [Fact]
    public async Task Broken_adapter_does_not_block_a_second_adapter_and_dead_letter_can_be_retried()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var brokenSystem = $"BROKEN_{suffix}";
        var workingSystem = $"WORKING_{suffix}";
        var settings = new { eventTypes = new[] { "logistic_unit.created" } };
        var brokenId = await CreateEndpoint(brokenSystem, $"http://api:8080/api/v1/integration-inbox/MISSING_{suffix}", settings);
        var workingId = await CreateEndpoint(workingSystem, $"http://api:8080/api/v1/integration-inbox/{workingSystem}", settings);

        var unit = await client.PostAsJsonAsync("/api/v1/logistic-units", new
        {
            code = $"BOX-OPS-{Guid.NewGuid():N}", type = "BOX"
        });
        Assert.Equal(HttpStatusCode.Created, unit.StatusCode);

        var broken = await WaitFor(brokenId, "DeadLetter");
        Assert.Equal("HTTP_404", broken.GetProperty("lastErrorCode").GetString());
        Assert.Equal("Delivered", (await WaitFor(workingId, "Delivered")).GetProperty("status").GetString());

        var configure = await client.PostAsJsonAsync($"/api/v1/integration-endpoints/{brokenId}/configuration", new
        {
            baseAddress = $"http://api:8080/api/v1/integration-inbox/{brokenSystem}",
            settings
        });
        Assert.Equal(HttpStatusCode.OK, configure.StatusCode);
        var retry = await client.PostAsync(
            $"/api/v1/integration-endpoints/{brokenId}/deliveries/{broken.GetProperty("id").GetGuid()}/retry", null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal("Delivered", (await WaitFor(brokenId, "Delivered")).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/v1/integration-endpoints/{brokenId}/enabled", new { enabled = false })).StatusCode);
        var endpoints = await client.GetFromJsonAsync<JsonElement>("/api/v1/integration-endpoints");
        var disabled = endpoints.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == brokenId);
        Assert.False(disabled.GetProperty("enabled").GetBoolean());
        Assert.False(disabled.TryGetProperty("secretRef", out _));
        Assert.True(disabled.TryGetProperty("hasSecretRef", out _));
    }

    [Fact]
    public async Task Regulatory_gateway_is_a_single_tenant_mode()
    {
        foreach (var mode in new[] { "ONE_C", "DIRECT_IS_MPT", "NONE" })
        {
            var response = await client.PostAsJsonAsync("/api/v1/integration-settings/regulatory-gateway", new { mode });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(mode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mode").GetString());
        }
        var invalid = await client.PostAsJsonAsync("/api/v1/integration-settings/regulatory-gateway", new { mode = "BOTH" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private async Task<Guid> CreateEndpoint(string system, string baseAddress, object settings)
    {
        var response = await client.PostAsJsonAsync("/api/v1/integration-endpoints", new
        {
            system, adapter = "WEBHOOK", baseAddress, settings, enabled = true
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> WaitFor(Guid endpointId, string status)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var deliveries = await client.GetFromJsonAsync<JsonElement>($"/api/v1/integration-endpoints/{endpointId}/deliveries");
            var found = deliveries.EnumerateArray().FirstOrDefault(x => x.GetProperty("status").GetString() == status);
            if (found.ValueKind != JsonValueKind.Undefined) return found;
            await Task.Delay(250);
        }
        return default;
    }
}
