using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UnitAtlas.Infrastructure.Integrations.Epcis;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class EpcisHttpTests
{
    private readonly HttpClient client = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("UNITATLAS_TEST_URL") ?? "http://host.docker.internal:8080")
    };

    [Fact]
    public async Task Export_and_capture_cover_object_and_aggregation_events()
    {
        var exported = await client.GetFromJsonAsync<JsonElement>("/api/v1/epcis/documents");
        Assert.Equal("EPCISDocument", exported.GetProperty("type").GetString());
        Assert.Contains(exported.GetProperty("epcisBody").GetProperty("eventList").EnumerateArray(),
            item => item.GetProperty("type").GetString() == "ObjectEvent");

        var eventId = Guid.NewGuid();
        var historicalTime = DateTimeOffset.Parse("2026-08-15T08:00:00Z");
        var capture = await client.PostAsJsonAsync("/api/v1/epcis/documents", Document(new
        {
            type = "ObjectEvent", eventID = $"urn:uuid:{eventId}", eventTime = historicalTime,
            eventTimeZoneOffset = "+00:00", epcList = new[] { "https://id.gs1.org/01/04871234567890/21/X200-260815-00042" },
            action = "OBSERVE", bizStep = "receiving"
        }));
        Assert.Equal(HttpStatusCode.Created, capture.StatusCode);
        var replayBody = await capture.RequestMessage!.Content!.ReadAsStringAsync();
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/epcis/documents")
        {
            Content = new StringContent(replayBody, System.Text.Encoding.UTF8, "application/json")
        };
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(replay)).StatusCode);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var parentCode = $"PALLET-EPCIS-{suffix}";
        var childCode = $"BOX-EPCIS-{suffix}";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/logistic-units", new { code = parentCode, type = "PALLET" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/logistic-units", new { code = childCode, type = "BOX" })).StatusCode);
        var aggregate = await client.PostAsJsonAsync("/api/v1/epcis/documents", Document(new
        {
            type = "AggregationEvent", eventID = $"urn:uuid:{Guid.NewGuid()}", eventTime = DateTimeOffset.UtcNow,
            eventTimeZoneOffset = "+00:00", parentID = $"urn:unitatlas:logistic-unit:{parentCode}",
            childEPCs = new[] { $"urn:unitatlas:logistic-unit:{childCode}" }, action = "ADD", bizStep = "packing"
        }));
        Assert.Equal(HttpStatusCode.Created, aggregate.StatusCode);
        var contents = await client.GetFromJsonAsync<JsonElement>($"/api/v1/logistic-units/{parentCode}");
        Assert.Contains(contents.GetProperty("children").EnumerateArray(), x => x.GetProperty("code").GetString() == childCode);
    }

    private static Dictionary<string, object> Document(object value) => new()
    {
        ["@context"] = EpcisMapper.Context,
        ["type"] = "EPCISDocument",
        ["schemaVersion"] = "2.0",
        ["creationDate"] = DateTimeOffset.UtcNow,
        ["epcisBody"] = new { eventList = new[] { value } }
    };
}
