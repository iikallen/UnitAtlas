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
        var device = await Enroll();
        var bootstrap = await _client.GetFromJsonAsync<JsonElement>("/api/v1/capture/bootstrap");
        Assert.Equal("Atlas Manufacturing", bootstrap.GetProperty("tenant").GetProperty("name").GetString());
        Assert.Equal(device.Code, bootstrap.GetProperty("device").GetProperty("code").GetString());
        Assert.Equal(device.StationId, bootstrap.GetProperty("station").GetProperty("id").GetGuid());
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
        var command = new { commandId, deviceId = device.Code, commandType = "AGGREGATE", parentCode = box, action = "ADD", unitAtlasIds = new[] { atlasId } };
        var first = await _client.PostAsJsonAsync("/api/v1/capture/sync", command);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var replay = await _client.PostAsJsonAsync("/api/v1/capture/sync", command);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean());

        var conflict = await _client.PostAsJsonAsync("/api/v1/capture/sync", new { commandId = Guid.NewGuid(), deviceId = device.Code, commandType = "AGGREGATE", parentCode = otherBox, action = "ADD", unitAtlasIds = new[] { atlasId } });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("CHILD_ALREADY_AGGREGATED", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var resolved = await (await _client.PostAsJsonAsync("/api/v1/capture/resolve", new { code = atlasId })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(box, resolved.GetProperty("serverParent").GetString());
        var packaging = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/logistic-units/{box}");
        var aggregation = packaging.GetProperty("events")[0];
        Assert.Equal(device.DeviceId, aggregation.GetProperty("deviceId").GetGuid());
        Assert.Equal(device.StationId, aggregation.GetProperty("stationId").GetGuid());
        Assert.Equal(device.ReadPointId, aggregation.GetProperty("readPointId").GetGuid());
        Assert.Equal(device.BusinessLocationId, aggregation.GetProperty("businessLocationId").GetGuid());

        var forged = await _client.PostAsJsonAsync("/api/v1/capture/sync", new
        {
            commandId = Guid.NewGuid(), deviceId = "FORGED-DEVICE", commandType = "AGGREGATE",
            parentCode = box, action = "DELETE", unitAtlasIds = new[] { atlasId }
        });
        Assert.Equal(HttpStatusCode.Conflict, forged.StatusCode);
        Assert.Equal("DEVICE_COMMAND_MISMATCH", (await forged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var sessions = await _client.GetFromJsonAsync<JsonElement>("/api/v1/device-sessions");
        var sessionId = sessions.EnumerateArray().Single(x => x.GetProperty("deviceId").GetGuid() == device.DeviceId).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"/api/v1/device-sessions/{sessionId}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/capture/bootstrap")).StatusCode);
    }

    [Fact]
    public async Task Quality_and_move_use_the_same_idempotent_trace_ledger()
    {
        var device = await Enroll();
        var bootstrap = await _client.GetFromJsonAsync<JsonElement>("/api/v1/capture/bootstrap");
        var syncToken = bootstrap.GetProperty("syncToken").GetString();
        var productId = bootstrap.GetProperty("products")[0].GetProperty("id").GetGuid();
        var unitResponse = await _client.PostAsJsonAsync("/api/v1/units", new { productId, serial = $"QC-{Guid.NewGuid():N}", lot = "CAPTURE-QC" });
        var atlasId = (await unitResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("atlasId").GetString()!;
        var commandId = Guid.NewGuid();
        var quality = new { commandId, deviceId = device.Code, unitAtlasId = atlasId, outcome = "PASS", location = "QC Station" };
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/capture/quality", quality)).StatusCode);
        var replay = await _client.PostAsJsonAsync("/api/v1/capture/quality", quality);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean());

        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/capture/move", new
        {
            commandId = Guid.NewGuid(), deviceId = device.Code, unitAtlasId = atlasId, to = "Rack 15"
        })).StatusCode);
        var passport = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/units/{atlasId}");
        Assert.Equal("In warehouse", passport.GetProperty("state").GetProperty("status").GetString());
        Assert.Equal("Rack 15", passport.GetProperty("state").GetProperty("location").GetString());
        var latest = passport.GetProperty("events")[0];
        Assert.Equal(device.DeviceId, latest.GetProperty("deviceId").GetGuid());
        Assert.Equal(device.StationId, latest.GetProperty("stationId").GetGuid());
        Assert.Equal(device.ReadPointId, latest.GetProperty("readPointId").GetGuid());
        Assert.Equal(device.BusinessLocationId, latest.GetProperty("businessLocationId").GetGuid());

        var changes = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/capture/changes?after={syncToken}");
        Assert.Contains(changes.GetProperty("changes").EnumerateArray(), change =>
            change.GetProperty("resourceType").GetString() == "UNIT"
            && change.GetProperty("resourceId").GetString() == atlasId);
        Assert.True(long.Parse(changes.GetProperty("nextToken").GetString()!) > long.Parse(syncToken!));
    }

    private async Task<EnrolledDevice> Enroll()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/capture/bootstrap")).StatusCode);
        var sites = await _client.GetFromJsonAsync<JsonElement>("/api/v1/sites");
        var locations = await _client.GetFromJsonAsync<JsonElement>("/api/v1/locations");
        var siteId = sites[0].GetProperty("id").GetGuid();
        var locationId = locations.EnumerateArray().First(x => x.GetProperty("siteId").GetGuid() == siteId).GetProperty("id").GetGuid();
        var code = $"CAP-{Guid.NewGuid():N}".ToUpperInvariant();
        var deviceResponse = await _client.PostAsJsonAsync("/api/v1/devices", new { code, name = "Capture test", platform = "ANDROID" });
        Assert.Equal(HttpStatusCode.Created, deviceResponse.StatusCode);
        var deviceId = (await deviceResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var stationResponse = await _client.PostAsJsonAsync("/api/v1/stations", new
        {
            code = $"ST-{Guid.NewGuid():N}", name = "Test station", siteId,
            readPointId = locationId, businessLocationId = locationId
        });
        Assert.Equal(HttpStatusCode.Created, stationResponse.StatusCode);
        var stationId = (await stationResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var enrollmentResponse = await _client.PostAsJsonAsync("/api/v1/device-enrollments", new { deviceId, stationId });
        Assert.Equal(HttpStatusCode.Created, enrollmentResponse.StatusCode);
        var enrollment = await enrollmentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var enrollmentCode = enrollment.GetProperty("enrollmentCode").GetString();
        var enrollResponse = await _client.PostAsJsonAsync("/api/v1/capture/enroll", new { deviceCode = code, enrollmentCode });
        Assert.Equal(HttpStatusCode.Created, enrollResponse.StatusCode);
        var session = await enrollResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/v1/capture/enroll", new { deviceCode = code, enrollmentCode })).StatusCode);
        _client.DefaultRequestHeaders.Add("X-UnitAtlas-Device-Session", session.GetProperty("sessionToken").GetString());
        return new EnrolledDevice(code, deviceId, stationId, locationId, locationId);
    }

    private sealed record EnrolledDevice(string Code, Guid DeviceId, Guid StationId, Guid ReadPointId, Guid BusinessLocationId);
}
