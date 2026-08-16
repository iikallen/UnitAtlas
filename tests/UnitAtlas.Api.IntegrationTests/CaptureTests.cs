using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class CaptureTests
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("UNITATLAS_TEST_URL") ?? "http://host.docker.internal:8080")
    };

    [Fact]
    public async Task Bootstrap_and_offline_command_replay_keep_server_authoritative()
    {
        var bootstrap = await _client.GetFromJsonAsync<JsonElement>("/api/v1/capture/bootstrap");
        Assert.Equal("Atlas Manufacturing", bootstrap.GetProperty("tenant").GetProperty("name").GetString());
        var productId = bootstrap.GetProperty("products")[0].GetProperty("id").GetGuid();
        var serial = $"CAP-{Guid.NewGuid():N}";
        var unitResponse = await _client.PostAsJsonAsync("/api/v1/units", new { productId, serial, lot = "CAPTURE-LOT" });
        Assert.Equal(HttpStatusCode.Created, unitResponse.StatusCode);
        var atlasId = (await unitResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("atlasId").GetString()!;
        var box = $"BOX-{Guid.NewGuid():N}";
        var otherBox = $"BOX-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code = box, type = "BOX" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code = otherBox, type = "BOX" })).StatusCode);

        var commandId = Guid.NewGuid();
        var command = new { commandId, deviceId = "capture-test", commandType = "AGGREGATE", parentCode = box, action = "ADD", unitAtlasIds = new[] { atlasId } };
        var first = await _client.PostAsJsonAsync("/api/v1/capture/sync", command);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var replay = await _client.PostAsJsonAsync("/api/v1/capture/sync", command);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean());

        var conflict = await _client.PostAsJsonAsync("/api/v1/capture/sync", new { commandId = Guid.NewGuid(), deviceId = "capture-test", commandType = "AGGREGATE", parentCode = otherBox, action = "ADD", unitAtlasIds = new[] { atlasId } });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("CHILD_ALREADY_AGGREGATED", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var resolved = await (await _client.PostAsJsonAsync("/api/v1/capture/resolve", new { code = atlasId })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(box, resolved.GetProperty("serverParent").GetString());
    }
}
