using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class ConcretePilotProfileTests
{
    private const string Profile = "ONEC_UPP_KZ_1_3_HTTP_JSON_V1";
    private readonly HttpClient client = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("UNITATLAS_TEST_URL") ?? "http://host.docker.internal:8080")
    };

    [Fact]
    public async Task Order_5812_runs_the_automated_factory_pilot_flow()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var system = $"UPPKZ_{suffix[..8]}".ToUpperInvariant();
        var endpointResponse = await client.PostAsJsonAsync("/api/v1/integration-endpoints", new
        {
            system,
            adapter = "ONE_C",
            baseAddress = $"http://api:8080/api/v1/integration-inbox/{system}",
            settings = new
            {
                profile = Profile,
                eventTypes = new[] { "shipment.recorded" }
            },
            enabled = true
        });
        Assert.Equal(HttpStatusCode.Created, endpointResponse.StatusCode);
        var endpointId = (await endpointResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var productExternalId = $"product-{suffix}";
        Assert.Equal(HttpStatusCode.Created, (await Send(system, $"product-{suffix}", new
        {
            type = "product.upsert",
            data = new { externalId = productExternalId, sku = $"KZ-{suffix[..10]}", name = "Pilot product", gtin = Gtin(suffix) }
        })).StatusCode);

        var setup = await client.GetFromJsonAsync<JsonElement>("/api/v1/print-setup");
        var templateId = Find(setup.GetProperty("templates"), "code", "GS1_DATAMATRIX_UNIT").GetProperty("id").GetGuid();
        var logisticsTemplateId = Find(setup.GetProperty("templates"), "code", "GS1_LOGISTICS_LABEL").GetProperty("id").GetGuid();
        var printerId = Find(setup.GetProperty("printers"), "code", "DEMO-EDGE").GetProperty("id").GetGuid();
        var profileResponse = await client.PostAsJsonAsync("/api/v1/print-profiles", new
        {
            code = $"UPPKZ-{suffix[..8]}", identifierMode = "GS1", gs1CompanyPrefix = "487123"
        });
        Assert.Equal(HttpStatusCode.Created, profileResponse.StatusCode);
        var printProfileId = (await profileResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var messageId = $"production-order-5812-{suffix}";
        var order = new
        {
            type = "production_order.completed",
            data = new
            {
                externalId = $"5812-{suffix}", productExternalId, lot = $"L{suffix[..10]}",
                serialPrefix = $"P{suffix[..6]}", quantity = 100, occurredAt = "2026-08-16T10:00:00Z",
                label = new { templateId, profileId = printProfileId, printerId }
            }
        };
        var production = await Send(system, messageId, order);
        Assert.Equal(HttpStatusCode.Created, production.StatusCode);
        var result = await production.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100, result.GetProperty("quantity").GetInt32());
        var units = result.GetProperty("units").EnumerateArray().Select(x => new
        {
            AtlasId = x.GetProperty("atlasId").GetString()!,
            Serial = x.GetProperty("serial").GetString()!
        }).ToArray();
        Assert.Equal(100, units.Length);
        Assert.Equal(100, units.Select(x => x.AtlasId).Distinct().Count());
        Assert.Equal(100, units.Select(x => x.Serial).Distinct().Count());
        var printJobId = result.GetProperty("printJobId").GetGuid();

        var replay = await Send(system, messageId, order);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(printJobId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printJobId").GetGuid());
        var externalDocumentReplay = await Send(system, $"{messageId}-retry", order);
        Assert.Equal(HttpStatusCode.Created, externalDocumentReplay.StatusCode);
        var referencedOrder = await externalDocumentReplay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(referencedOrder.GetProperty("created").GetBoolean());
        Assert.Equal(printJobId, referencedOrder.GetProperty("printJobId").GetGuid());
        var changedOrder = await Send(system, $"{messageId}-changed", new
        {
            type = "production_order.completed",
            data = new
            {
                externalId = $"5812-{suffix}", productExternalId, lot = $"L{suffix[..10]}",
                serialPrefix = $"P{suffix[..6]}", quantity = 99, occurredAt = "2026-08-16T10:00:00Z",
                label = new { templateId, profileId = printProfileId, printerId }
            }
        });
        Assert.Equal(HttpStatusCode.Conflict, changedOrder.StatusCode);
        Assert.Equal("ONE_C_PRODUCTION_ORDER_CONFLICT",
            (await changedOrder.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var job = await client.GetFromJsonAsync<JsonElement>($"/api/v1/print-jobs/{printJobId}");
        Assert.Equal(100, job.GetProperty("items").GetArrayLength());
        Assert.All(job.GetProperty("items").EnumerateArray(), item => Assert.StartsWith("01", item.GetProperty("payload").GetString()));
        var device = await Enroll();
        var bypass = await client.PostAsJsonAsync($"/api/v1/units/{units[0].AtlasId}/events", new
        {
            eventType = "COMMISSIONED", location = "Manual bypass", idempotencyKey = $"bypass:{suffix}"
        });
        Assert.Equal(HttpStatusCode.Conflict, bypass.StatusCode);
        Assert.Equal("COMMISSIONING_REQUIRES_CAPTURE", (await bypass.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var notPrinted = await client.PostAsJsonAsync("/api/v1/capture/production", new
        {
            commandId = Guid.NewGuid(), deviceId = device, scannedCode = units[0].Serial, location = "Pilot line"
        });
        Assert.Equal(HttpStatusCode.Conflict, notPrinted.StatusCode);
        Assert.Equal("LABEL_NOT_PRINTED", (await notPrinted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/v1/print-jobs/{printJobId}/attempts", new { status = "DISPATCHED" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/v1/print-jobs/{printJobId}/attempts", new { status = "PRINTED" })).StatusCode);

        for (var index = 0; index < units.Length; index++)
        {
            var commandId = Guid.NewGuid();
            var confirmation = new
            {
                commandId, deviceId = device, scannedCode = units[index].Serial, location = "Pilot line",
                occurredAt = "2026-08-16T11:00:00Z"
            };
            Assert.Equal(HttpStatusCode.Created,
                (await client.PostAsJsonAsync("/api/v1/capture/production", confirmation)).StatusCode);
            if (index == 0)
            {
                var replayConfirmation = await client.PostAsJsonAsync("/api/v1/capture/production", confirmation);
                Assert.Equal(HttpStatusCode.Created, replayConfirmation.StatusCode);
                Assert.True((await replayConfirmation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean());
                var repeatedScan = await client.PostAsJsonAsync("/api/v1/capture/production", new
                {
                    commandId = Guid.NewGuid(), deviceId = device, scannedCode = units[index].Serial,
                    location = "Pilot line", occurredAt = "2026-08-16T11:00:00Z"
                });
                Assert.Equal(HttpStatusCode.OK, repeatedScan.StatusCode);
                Assert.True((await repeatedScan.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean());
            }
        }
        Assert.Equal("Commissioned", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/units/{units[0].AtlasId}"))
            .GetProperty("state").GetProperty("status").GetString());

        var boxes = Enumerable.Range(1, 10).Select(index => $"BOX-5812-{suffix[..8]}-{index:D2}").ToArray();
        foreach (var box in boxes)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/logistic-units", new { code = box, type = "BOX" })).StatusCode);
        var pallet = $"PALLET-5812-{suffix[..8]}";
        var sscc = Sscc(suffix);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/logistic-units", new { code = pallet, type = "PALLET", sscc })).StatusCode);
        for (var index = 0; index < boxes.Length; index++)
        {
            var packed = await client.PostAsJsonAsync("/api/v1/capture/sync", new
            {
                commandId = Guid.NewGuid(), deviceId = device, commandType = "AGGREGATE",
                parentCode = boxes[index], action = "ADD", unitAtlasIds = units.Skip(index * 10).Take(10).Select(x => x.AtlasId).ToArray()
            });
            Assert.Equal(HttpStatusCode.Created, packed.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/capture/sync", new
        {
            commandId = Guid.NewGuid(), deviceId = device, commandType = "AGGREGATE",
            parentCode = pallet, action = "ADD", logisticUnitCodes = boxes
        })).StatusCode);
        var logisticsJobResponse = await client.PostAsJsonAsync("/api/v1/print-jobs", new
        {
            templateId = logisticsTemplateId, profileId = printProfileId, printerId,
            entityType = "LOGISTIC_UNIT", code = pallet, copies = 1, idempotencyKey = $"pallet-label:{suffix}"
        });
        Assert.Equal(HttpStatusCode.Created, logisticsJobResponse.StatusCode);
        var logisticsJobId = (await logisticsJobResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var logisticsJob = await client.GetFromJsonAsync<JsonElement>($"/api/v1/print-jobs/{logisticsJobId}");
        Assert.Equal($"00{sscc}", logisticsJob.GetProperty("items")[0].GetProperty("payload").GetString());
        var conflict = await client.PostAsJsonAsync("/api/v1/capture/sync", new
        {
            commandId = Guid.NewGuid(), deviceId = device, commandType = "AGGREGATE",
            parentCode = boxes[1], action = "ADD", unitAtlasIds = new[] { units[0].AtlasId }
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("CHILD_ALREADY_AGGREGATED", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var shipment = await Send(system, $"shipment-{suffix}", new
        {
            type = "shipment.recorded",
            data = new
            {
                externalId = $"shipment-5812-{suffix}", unitExternalId = $"5812-{suffix}:0001",
                location = "Outbound dock", occurredAt = "2026-08-16T12:00:00Z"
            }
        });
        Assert.Equal(HttpStatusCode.Created, shipment.StatusCode);
        Assert.Equal("Shipped", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/units/{Uri.EscapeDataString(units[0].AtlasId)}"))
            .GetProperty("state").GetProperty("status").GetString());

        JsonElement delivered = default;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var deliveries = await client.GetFromJsonAsync<JsonElement>($"/api/v1/integration-endpoints/{endpointId}/deliveries");
            delivered = deliveries.EnumerateArray().FirstOrDefault(x =>
                x.GetProperty("type").GetString() == "shipment.recorded"
                && x.GetProperty("status").GetString() == "Delivered");
            if (delivered.ValueKind != JsonValueKind.Undefined) break;
            await Task.Delay(250);
        }
        Assert.NotEqual(JsonValueKind.Undefined, delivered.ValueKind);
        Assert.Equal("Delivered", delivered.GetProperty("status").GetString());
        var epcis = await client.GetFromJsonAsync<JsonElement>("/api/v1/epcis/documents");
        Assert.Contains(epcis.GetProperty("epcisBody").GetProperty("eventList").EnumerateArray(),
            item => item.GetProperty("type").GetString() == "AggregationEvent");
    }

    private async Task<string> Enroll()
    {
        var site = (await client.GetFromJsonAsync<JsonElement>("/api/v1/sites"))[0];
        var siteId = site.GetProperty("id").GetGuid();
        var locations = await client.GetFromJsonAsync<JsonElement>("/api/v1/locations");
        var locationId = locations.EnumerateArray().First(x => x.GetProperty("siteId").GetGuid() == siteId).GetProperty("id").GetGuid();
        var code = $"UPPKZ-{Guid.NewGuid():N}".ToUpperInvariant();
        var device = await client.PostAsJsonAsync("/api/v1/devices", new { code, name = "UPP KZ pilot test", platform = "ANDROID" });
        var deviceId = (await device.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var station = await client.PostAsJsonAsync("/api/v1/stations", new
        {
            code = $"UPPKZ-ST-{Guid.NewGuid():N}", name = "UPP KZ pilot station", siteId,
            readPointId = locationId, businessLocationId = locationId
        });
        var stationId = (await station.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var enrollment = await client.PostAsJsonAsync("/api/v1/device-enrollments", new { deviceId, stationId });
        var enrollmentCode = (await enrollment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enrollmentCode").GetString();
        var session = await client.PostAsJsonAsync("/api/v1/capture/enroll", new { deviceCode = code, enrollmentCode });
        client.DefaultRequestHeaders.Add("X-UnitAtlas-Device-Session",
            (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionToken").GetString());
        return code;
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

    private static JsonElement Find(JsonElement rows, string property, string value) =>
        rows.EnumerateArray().Single(row => row.GetProperty(property).GetString() == value);

    private static string Gtin(string seed)
    {
        var digits = new string(seed.Where(char.IsDigit).ToArray()).PadRight(6, '0')[..6];
        var body = $"0487123{digits}";
        var sum = body.Select((digit, index) => (digit - '0') * ((14 - index) % 2 == 0 ? 3 : 1)).Sum();
        return body + (10 - sum % 10) % 10;
    }

    private static string Sscc(string seed)
    {
        var digits = new string(seed.Where(char.IsDigit).ToArray()).PadRight(10, '0')[..10];
        var body = $"0487123{digits}";
        var sum = body.Select((digit, index) => (digit - '0') * ((18 - index) % 2 == 0 ? 3 : 1)).Sum();
        return body + (10 - sum % 10) % 10;
    }
}
