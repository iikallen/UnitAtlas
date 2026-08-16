using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class PrintingTests
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("UNITATLAS_TEST_URL") ?? "http://host.docker.internal:8080")
    };

    [Fact]
    public async Task Internal_unit_job_is_idempotent_and_tracks_attempts()
    {
        var setup = await _client.GetFromJsonAsync<JsonElement>("/api/v1/print-setup");
        var template = Find(setup.GetProperty("templates"), "code", "INTERNAL_UNIT_QR").GetProperty("id").GetGuid();
        var profile = Find(setup.GetProperty("profiles"), "code", "DEMO-INTERNAL").GetProperty("id").GetGuid();
        var printer = Find(setup.GetProperty("printers"), "code", "DEMO-EDGE").GetProperty("id").GetGuid();
        var key = $"print:{Guid.NewGuid()}";
        var request = new { templateId = template, profileId = profile, printerId = printer, entityType = "UNIT", code = "UA-KZ-2026-0000058219", copies = 1, idempotencyKey = key };

        var created = await _client.PostAsJsonAsync("/api/v1/print-jobs", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var replay = await _client.PostAsJsonAsync("/api/v1/print-jobs", request);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean());
        var conflict = await _client.PostAsJsonAsync("/api/v1/print-jobs", new { templateId = template, profileId = profile, printerId = printer, entityType = "UNIT", code = "UA-KZ-2026-0000058219", copies = 2, idempotencyKey = key });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync($"/api/v1/print-jobs/{id}/attempts", new { status = "PRINTED" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync($"/api/v1/print-jobs/{id}/attempts", new { status = "DISPATCHED" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync($"/api/v1/print-jobs/{id}/attempts", new { status = "PRINTED" })).StatusCode);
        var job = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/print-jobs/{id}");
        Assert.Equal("PRINTED", job.GetProperty("status").GetString());
        Assert.Equal("unitatlas:unit:UA-KZ-2026-0000058219", job.GetProperty("items")[0].GetProperty("payload").GetString());
        Assert.Equal(2, job.GetProperty("attempts").GetArrayLength());
    }

    [Fact]
    public async Task Gs1_profile_requires_a_real_prefix_and_matching_identifier()
    {
        var invalid = await _client.PostAsJsonAsync("/api/v1/print-profiles", new { code = $"BAD-{Guid.NewGuid():N}", identifierMode = "GS1" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var setup = await _client.GetFromJsonAsync<JsonElement>("/api/v1/print-setup");
        var template = Find(setup.GetProperty("templates"), "code", "GS1_DATAMATRIX_UNIT").GetProperty("id").GetGuid();
        var printer = Find(setup.GetProperty("printers"), "code", "DEMO-EDGE").GetProperty("id").GetGuid();
        var profileResponse = await _client.PostAsJsonAsync("/api/v1/print-profiles", new { code = $"GS1-{Guid.NewGuid():N}", identifierMode = "GS1", gs1CompanyPrefix = "487123" });
        Assert.Equal(HttpStatusCode.Created, profileResponse.StatusCode);
        var profile = (await profileResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var jobResponse = await _client.PostAsJsonAsync("/api/v1/print-jobs", new { templateId = template, profileId = profile, printerId = printer, entityType = "UNIT", code = "UA-KZ-2026-0000058219", copies = 1, idempotencyKey = $"gs1:{Guid.NewGuid()}" });
        Assert.Equal(HttpStatusCode.Created, jobResponse.StatusCode);
        var jobId = (await jobResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var job = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/print-jobs/{jobId}");
        Assert.Equal("(01)04871234567890(10)LOT-260815-A(21)X200-260815-00042", job.GetProperty("items")[0].GetProperty("humanReadable").GetString());
    }

    [Fact]
    public async Task Internal_logistics_label_uses_the_existing_logistic_unit()
    {
        var code = $"PAL-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/logistic-units", new { code, type = "PALLET" })).StatusCode);
        var setup = await _client.GetFromJsonAsync<JsonElement>("/api/v1/print-setup");
        var template = Find(setup.GetProperty("templates"), "code", "INTERNAL_LOGISTICS_QR").GetProperty("id").GetGuid();
        var profile = Find(setup.GetProperty("profiles"), "code", "DEMO-INTERNAL").GetProperty("id").GetGuid();
        var printer = Find(setup.GetProperty("printers"), "code", "DEMO-EDGE").GetProperty("id").GetGuid();
        var response = await _client.PostAsJsonAsync("/api/v1/print-jobs", new { templateId = template, profileId = profile, printerId = printer, entityType = "LOGISTIC_UNIT", code, copies = 1, idempotencyKey = $"logistic:{Guid.NewGuid()}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var jobId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var job = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/print-jobs/{jobId}");
        Assert.Equal($"unitatlas:logistic_unit:{code}", job.GetProperty("items")[0].GetProperty("payload").GetString());
    }

    private static JsonElement Find(JsonElement rows, string property, string value) =>
        rows.EnumerateArray().Single(row => row.GetProperty(property).GetString() == value);
}
