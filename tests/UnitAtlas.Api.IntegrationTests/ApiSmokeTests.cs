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
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/sites")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/units/UA-KZ-2026-0000058219")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/passports/UA-KZ-2026-0000058219")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/units/UA-KZ-2026-0000058219/events")).StatusCode);
        var publicPassport = await _client.GetFromJsonAsync<JsonElement>("/api/public/passports/demo-x200-58219");
        Assert.Equal("verified", publicPassport.GetProperty("authenticity").GetString());
        Assert.False(publicPassport.ToString().Contains("actor", StringComparison.OrdinalIgnoreCase));
        Assert.False(publicPassport.ToString().Contains("location", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Delayed_event_is_recorded_without_rolling_back_projection()
    {
        var key = $"integration:late:{Guid.NewGuid()}";
        var request = new
        {
            eventType = "QUALITY_PASSED",
            location = "Delayed QC",
            actor = "integration-test",
            idempotencyKey = key,
            occurredAt = "2026-08-15T09:30:00Z"
        };
        var response = await _client.PostAsJsonAsync("/api/v1/units/UA-KZ-2026-0000058219/events", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var firstId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal('7', firstId.ToString()[14]);

        var replay = await _client.PostAsJsonAsync("/api/v1/units/UA-KZ-2026-0000058219/events", request);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(firstId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

        var conflict = await _client.PostAsJsonAsync("/api/v1/units/UA-KZ-2026-0000058219/events", new
        {
            request.eventType,
            location = "Different body",
            request.actor,
            request.idempotencyKey,
            request.occurredAt
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var passport = await _client.GetFromJsonAsync<JsonElement>("/api/v1/units/UA-KZ-2026-0000058219");
        Assert.Equal("Shipped", passport.GetProperty("state").GetProperty("status").GetString());
        Assert.Contains(passport.GetProperty("events").EnumerateArray(), item =>
            item.GetProperty("eventType").GetString() == "QUALITY_PASSED"
            && item.GetProperty("actor").GetString() == "integration-test");
    }

    [Fact]
    public async Task Missing_required_strings_return_validation_errors()
    {
        var product = await _client.PostAsJsonAsync("/api/v1/products", new { });
        var traceEvent = await _client.PostAsJsonAsync("/api/v1/units/UA-KZ-2026-0000058219/events", new { });

        Assert.Equal(HttpStatusCode.BadRequest, product.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, traceEvent.StatusCode);
        var problem = await traceEvent.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Errors_headers_and_cursor_pagination_have_stable_contracts()
    {
        var missing = await _client.GetAsync("/api/v1/units/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNIT_NOT_FOUND", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("nosniff", missing.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(missing.Headers.Contains("Content-Security-Policy"));

        var first = await _client.GetAsync("/api/v1/units?limit=2");
        var firstPage = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, firstPage.GetArrayLength());
        var cursor = first.Headers.GetValues("X-Next-Cursor").Single();
        var secondPage = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/units?limit=2&cursor={Uri.EscapeDataString(cursor)}");
        Assert.DoesNotContain(secondPage.EnumerateArray(), item =>
            firstPage.EnumerateArray().Any(firstItem => firstItem.GetProperty("atlasId").GetString() == item.GetProperty("atlasId").GetString()));
    }

    [Fact]
    public async Task Structured_locations_cannot_cross_the_tenant_boundary()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/units/UA-KZ-2026-0000058219/events", new
        {
            eventType = "PACKED",
            location = "Warehouse A",
            idempotencyKey = $"integration:location:{Guid.NewGuid()}",
            businessLocationId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Tenant_membership_and_rls_block_cross_tenant_access()
    {
        using var secondTenantRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dashboard");
        secondTenantRequest.Headers.Add("X-Demo-Subject", "second.viewer");
        secondTenantRequest.Headers.Add("X-Demo-Tenant", "22222222-2222-2222-2222-222222222222");
        var secondTenantResponse = await _client.SendAsync(secondTenantRequest);
        Assert.Equal(HttpStatusCode.OK, secondTenantResponse.StatusCode);
        Assert.Equal(1, (await secondTenantResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalUnits").GetInt32());

        using var mismatchedMembership = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dashboard");
        mismatchedMembership.Headers.Add("X-Demo-Subject", "demo.operator");
        mismatchedMembership.Headers.Add("X-Demo-Tenant", "22222222-2222-2222-2222-222222222222");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(mismatchedMembership)).StatusCode);

        using var viewerWrite = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products");
        viewerWrite.Headers.Add("X-Demo-Subject", "second.viewer");
        viewerWrite.Headers.Add("X-Demo-Tenant", "22222222-2222-2222-2222-222222222222");
        viewerWrite.Content = JsonContent.Create(new { sku = "DENIED", name = "Denied", gtin = "04870000000002" });
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(viewerWrite)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_event_requests_are_serialized_and_idempotent()
    {
        var sharedKey = $"integration:concurrent:{Guid.NewGuid()}";
        var sharedRequest = new
        {
            eventType = "MOVED_TO_WAREHOUSE",
            location = "Warehouse A",
            actor = "integration-test",
            idempotencyKey = sharedKey,
            occurredAt = "2026-08-16T12:00:00Z"
        };
        var replays = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => _client.PostAsJsonAsync("/api/v1/units/UA-KZ-2026-0000058220/events", sharedRequest)));
        Assert.All(replays, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var replayIds = await Task.WhenAll(replays.Select(async response =>
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()));
        Assert.Single(replayIds.Distinct());

        var distinct = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            _client.PostAsJsonAsync("/api/v1/units/UA-KZ-2026-0000058220/events", new
            {
                eventType = "MOVED_TO_WAREHOUSE",
                location = $"Warehouse {index}",
                idempotencyKey = $"integration:distinct:{Guid.NewGuid()}"
            })));
        Assert.All(distinct, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
    }
}
