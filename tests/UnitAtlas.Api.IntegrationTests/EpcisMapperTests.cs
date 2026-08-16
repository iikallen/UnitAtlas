using System.Text.Json;
using UnitAtlas.Infrastructure.Integrations.Epcis;

namespace UnitAtlas.Api.IntegrationTests;

public sealed class EpcisMapperTests
{
    [Fact]
    public void Export_maps_supported_events_and_real_identifiers()
    {
        var now = DateTimeOffset.Parse("2026-08-16T10:00:00+06:00");
        var document = EpcisMapper.Export(
            [new(Guid.Parse("11111111-1111-7111-8111-111111111111"), now, now, "SHIPPED", null, null, null, null,
                "UA-KZ-1", "04871234567890", "SERIAL 1")],
            [new(Guid.Parse("22222222-2222-7222-8222-222222222222"), now, now, "ADD",
                EpcisMapper.LogisticUnitIdentifier("PALLET-1", "123456789012345675"),
                [EpcisMapper.UnitIdentifier("UA-KZ-1", "04871234567890", "SERIAL 1")], null, null)], now);

        Assert.Equal(EpcisMapper.Context, document.GetProperty("@context").GetString());
        var events = document.GetProperty("epcisBody").GetProperty("eventList");
        Assert.Equal("ObjectEvent", events[0].GetProperty("type").GetString());
        Assert.Equal("https://id.gs1.org/01/04871234567890/21/SERIAL%201", events[0].GetProperty("epcList")[0].GetString());
        Assert.Equal("shipping", events[0].GetProperty("bizStep").GetString());
        Assert.Equal("AggregationEvent", events[1].GetProperty("type").GetString());
        Assert.Equal("https://id.gs1.org/00/123456789012345675", events[1].GetProperty("parentID").GetString());
    }

    [Fact]
    public void Import_rejects_unclaimed_event_types_and_maps_object_event()
    {
        var supported = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"EPCISDocument","epcisBody":{"eventList":[{
              "type":"ObjectEvent","eventID":"urn:uuid:33333333-3333-7333-8333-333333333333",
              "eventTime":"2026-08-16T10:00:00Z","eventTimeZoneOffset":"+00:00",
              "epcList":["urn:unitatlas:unit:UA-KZ-1"],"action":"OBSERVE","bizStep":"receiving"
            }]}}
            """);
        var value = Assert.IsType<EpcisInboundObjectEvent>(EpcisMapper.Import(supported));
        Assert.Equal("RECEIVED", value.EventType);

        var unsupported = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"EPCISDocument","epcisBody":{"eventList":[{
              "type":"TransformationEvent","eventID":"urn:uuid:44444444-4444-7444-8444-444444444444",
              "eventTime":"2026-08-16T10:00:00Z","eventTimeZoneOffset":"+00:00"
            }]}}
            """);
        Assert.Throws<EpcisMappingException>(() => EpcisMapper.Import(unsupported));
    }
}
