using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class ApiSmokeTests
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("UNITATLAS_TEST_URL") ?? "http://host.docker.internal:8080")
    };

    [Fact]
    public async Task Readiness_and_public_surface_are_available()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/units/UA-KZ-2026-0000058219")).StatusCode);
    }

    [Fact]
    public async Task Delayed_event_is_recorded_without_rolling_back_projection()
    {
        var key = $"integration:late:{Guid.NewGuid()}";
        var response = await _client.PostAsJsonAsync("/api/units/UA-KZ-2026-0000058219/events", new
        {
            eventType = "QUALITY_PASSED",
            location = "Delayed QC",
            actor = "integration-test",
            idempotencyKey = key,
            occurredAt = "2026-08-15T09:30:00Z"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var passport = await _client.GetFromJsonAsync<JsonElement>("/api/units/UA-KZ-2026-0000058219");
        Assert.Equal("Shipped", passport.GetProperty("state").GetProperty("status").GetString());
        Assert.Contains(passport.GetProperty("events").EnumerateArray(), item =>
            item.GetProperty("eventType").GetString() == "QUALITY_PASSED"
            && item.GetProperty("actor").GetString() == "integration-test");
    }

    [Fact]
    public async Task Missing_required_strings_return_validation_errors()
    {
        var product = await _client.PostAsJsonAsync("/api/products", new { });
        var traceEvent = await _client.PostAsJsonAsync("/api/units/UA-KZ-2026-0000058219/events", new { });

        Assert.Equal(HttpStatusCode.BadRequest, product.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, traceEvent.StatusCode);
    }
}
