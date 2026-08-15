using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UnitAtlas.Application.Packaging;
using UnitAtlas.Contracts;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class PackagingTests
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("UNITATLAS_TEST_URL") ?? "http://host.docker.internal:8080")
    };

    [Fact]
    public async Task Packaging_supports_nested_aggregation_idempotency_and_disaggregation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var box = $"BOX-{suffix}";
        var secondBox = $"BOX2-{suffix}";
        var pallet = $"PAL-{suffix}";
        var container = $"CONT-{suffix}";

        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code = box, type = "BOX" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code = secondBox, type = "BOX" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code = pallet, type = "PALLET", sscc = NewSscc() })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code = container, type = "CONTAINER" })).StatusCode);

        var key = $"packaging:add:{Guid.NewGuid()}";
        var addUnit = new
        {
            action = "ADD",
            idempotencyKey = key,
            unitAtlasIds = new[] { "UA-KZ-2026-0000058221" }
        };
        var first = await _client.PostAsJsonAsync($"/api/v1/logistic-units/{box}/aggregations", addUnit);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var replay = await _client.PostAsJsonAsync($"/api/v1/logistic-units/{box}/aggregations", addUnit);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var replayJson = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstId, replayJson.GetProperty("id").GetGuid());
        Assert.True(replayJson.GetProperty("duplicate").GetBoolean());

        var duplicateParent = await _client.PostAsJsonAsync($"/api/v1/logistic-units/{secondBox}/aggregations", new
        {
            action = "ADD",
            idempotencyKey = $"packaging:duplicate:{Guid.NewGuid()}",
            unitAtlasIds = new[] { "UA-KZ-2026-0000058221" }
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateParent.StatusCode);
        Assert.Equal("CHILD_ALREADY_AGGREGATED", (await duplicateParent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var nest = await _client.PostAsJsonAsync($"/api/v1/logistic-units/{pallet}/aggregations", new
        {
            action = "ADD",
            idempotencyKey = $"packaging:nest:{Guid.NewGuid()}",
            logisticUnitCodes = new[] { box }
        });
        Assert.Equal(HttpStatusCode.Created, nest.StatusCode);

        var nestPallet = await _client.PostAsJsonAsync($"/api/v1/logistic-units/{container}/aggregations", new
        {
            action = "ADD",
            idempotencyKey = $"packaging:nest-pallet:{Guid.NewGuid()}",
            logisticUnitCodes = new[] { pallet }
        });
        Assert.Equal(HttpStatusCode.Created, nestPallet.StatusCode);

        var containerState = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/logistic-units/{container}");
        Assert.Contains(containerState.GetProperty("children").EnumerateArray(), child =>
            child.GetProperty("code").GetString() == pallet);

        var cycle = await _client.PostAsJsonAsync($"/api/v1/logistic-units/{box}/aggregations", new
        {
            action = "ADD",
            idempotencyKey = $"packaging:cycle:{Guid.NewGuid()}",
            logisticUnitCodes = new[] { container }
        });
        Assert.Equal(HttpStatusCode.Conflict, cycle.StatusCode);
        Assert.Equal("AGGREGATION_CYCLE", (await cycle.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var boxState = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/logistic-units/{box}");
        Assert.Contains(boxState.GetProperty("children").EnumerateArray(), child =>
            child.GetProperty("code").GetString() == "UA-KZ-2026-0000058221");

        var remove = await _client.PostAsJsonAsync($"/api/v1/logistic-units/{box}/aggregations", new
        {
            action = "DELETE",
            idempotencyKey = $"packaging:delete:{Guid.NewGuid()}",
            unitAtlasIds = new[] { "UA-KZ-2026-0000058221" }
        });
        Assert.Equal(HttpStatusCode.Created, remove.StatusCode);

        var afterRemove = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/logistic-units/{box}");
        Assert.Empty(afterRemove.GetProperty("children").EnumerateArray());
        Assert.True(afterRemove.GetProperty("events").GetArrayLength() >= 2);
        Assert.Contains(afterRemove.GetProperty("events").EnumerateArray(), item =>
            item.GetProperty("action").GetString() == "DELETE"
            && item.GetProperty("unitAtlasIds").EnumerateArray().Any(child =>
                child.GetString() == "UA-KZ-2026-0000058221"));
    }

    [Fact]
    public async Task Packaging_rejects_invalid_sscc_and_self_cycles()
    {
        var code = $"BOX-{Guid.NewGuid():N}";
        var invalid = await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code, type = "BOX", sscc = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code, type = "BOX" })).StatusCode);
        var cycle = await _client.PostAsJsonAsync($"/api/v1/logistic-units/{code}/aggregations", new
        {
            action = "ADD",
            idempotencyKey = $"packaging:self:{Guid.NewGuid()}",
            logisticUnitCodes = new[] { code }
        });
        Assert.Equal(HttpStatusCode.Conflict, cycle.StatusCode);
        Assert.Equal("AGGREGATION_CYCLE", (await cycle.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Concurrent_inverse_edges_cannot_create_a_cycle()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var left = $"LEFT-{suffix}";
        var right = $"RIGHT-{suffix}";
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code = left, type = "BOX" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code = right, type = "BOX" })).StatusCode);

        var leftToRight = _client.PostAsJsonAsync($"/api/v1/logistic-units/{left}/aggregations", new
        {
            action = "ADD",
            idempotencyKey = $"packaging:race:left:{Guid.NewGuid()}",
            logisticUnitCodes = new[] { right }
        });
        var rightToLeft = _client.PostAsJsonAsync($"/api/v1/logistic-units/{right}/aggregations", new
        {
            action = "ADD",
            idempotencyKey = $"packaging:race:right:{Guid.NewGuid()}",
            logisticUnitCodes = new[] { left }
        });

        var responses = await Task.WhenAll(leftToRight, rightToLeft);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("AGGREGATION_CYCLE", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public void Aggregation_request_hash_is_unambiguous_and_uses_normalized_children()
    {
        var splitOne = Request(["a,b", "c"]);
        var splitTwo = Request(["a", "b,c"]);
        Assert.NotEqual(PackagingRules.ComputeRequestHash("BOX", splitOne), PackagingRules.ComputeRequestHash("BOX", splitTwo));

        var noisy = Request([" c ", null!, "a,b", "c"]);
        Assert.Equal(PackagingRules.ComputeRequestHash("BOX", splitOne), PackagingRules.ComputeRequestHash(" BOX ", noisy));

        static AggregationRequest Request(IReadOnlyCollection<string> children) =>
            new("ADD", "ignored", children, null, null, null, null, "unitatlas");
    }

    private static string NewSscc()
    {
        var body = Random.Shared.NextInt64(10_000_000_000_000_000).ToString("D17");
        var sum = body.Select((value, index) => (value - '0') * (index % 2 == 0 ? 3 : 1)).Sum();
        return body + (10 - sum % 10) % 10;
    }
}
