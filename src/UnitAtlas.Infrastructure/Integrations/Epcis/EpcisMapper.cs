using System.Globalization;
using System.Text.Json;

namespace UnitAtlas.Infrastructure.Integrations.Epcis;

public sealed record EpcisTraceSource(
    Guid EventId, DateTimeOffset EventTime, DateTimeOffset RecordTime, string EventType,
    string? BusinessStep, string? Disposition, string? ReadPoint, string? BusinessLocation,
    string AtlasId, string Gtin, string Serial);

public sealed record EpcisAggregationSource(
    Guid EventId, DateTimeOffset EventTime, DateTimeOffset RecordTime, string Action,
    string ParentIdentifier, IReadOnlyCollection<string> ChildIdentifiers,
    string? ReadPoint, string? BusinessLocation);

public abstract record EpcisInboundEvent(string EventId, DateTimeOffset EventTime, string? ReadPoint, JsonElement Raw);
public sealed record EpcisInboundObjectEvent(
    string EventId, DateTimeOffset EventTime, string? ReadPoint, JsonElement Raw,
    IReadOnlyCollection<string> Epcs, string EventType, string? BusinessStep, string? Disposition)
    : EpcisInboundEvent(EventId, EventTime, ReadPoint, Raw);
public sealed record EpcisInboundAggregationEvent(
    string EventId, DateTimeOffset EventTime, string? ReadPoint, JsonElement Raw,
    string Parent, IReadOnlyCollection<string> Children, string Action)
    : EpcisInboundEvent(EventId, EventTime, ReadPoint, Raw);

public static class EpcisMapper
{
    public const string Context = "https://ref.gs1.org/standards/epcis/epcis-context.jsonld";
    private static readonly HashSet<string> KnownBusinessSteps = new(StringComparer.OrdinalIgnoreCase)
    {
        "accepting", "arriving", "assembling", "collecting", "commissioning", "consigning", "creating_class_instance",
        "cycle_counting", "decommissioning", "departing", "destroying", "disassembling", "dispensing", "encoding",
        "entering_exiting", "holding", "inspecting", "installing", "killing", "loading", "other", "packing", "picking",
        "receiving", "removing", "repackaging", "repairing", "replacing", "reserving", "retail_selling", "shipping",
        "staging_outbound", "stock_taking", "stocking", "storing", "transporting", "unloading", "unpacking",
        "void_shipping", "sensor_reporting", "sampling"
    };
    private static readonly HashSet<string> KnownDispositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "container_closed", "damaged", "destroyed", "dispensed", "disposed", "encoded", "expired", "in_progress",
        "in_transit", "inactive", "non_sellable_other", "recalled", "reserved", "retail_sold", "returned",
        "sellable_accessible", "sellable_not_accessible", "stolen", "unknown", "available", "conformant", "container_open",
        "non_conformant", "unavailable"
    };

    public static JsonElement Export(
        IEnumerable<EpcisTraceSource> traces,
        IEnumerable<EpcisAggregationSource> aggregations,
        DateTimeOffset createdAt)
    {
        var events = new List<object>();
        events.AddRange(traces.Select(Map));
        events.AddRange(aggregations.Select(Map));
        return JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["@context"] = Context,
            ["type"] = "EPCISDocument",
            ["schemaVersion"] = "2.0",
            ["creationDate"] = createdAt,
            ["epcisBody"] = new Dictionary<string, object> { ["eventList"] = events }
        });
    }

    public static EpcisInboundEvent Import(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object
            || !document.TryGetProperty("type", out var documentType)
            || documentType.ValueKind != JsonValueKind.String
            || documentType.GetString() != "EPCISDocument"
            || !document.TryGetProperty("epcisBody", out var body)
            || !body.TryGetProperty("eventList", out var list)
            || list.ValueKind != JsonValueKind.Array
            || list.GetArrayLength() != 1)
            throw new EpcisMappingException("The capture subset requires one event in an EPCISDocument.");

        var value = list[0];
        var eventId = RequiredString(value, "eventID");
        var eventTime = RequiredDate(value, "eventTime");
        var readPoint = NestedId(value, "readPoint");
        return RequiredString(value, "type") switch
        {
            "ObjectEvent" => new EpcisInboundObjectEvent(
                eventId, eventTime, readPoint, value.Clone(), RequiredStrings(value, "epcList"),
                ToUnitAtlasEventType(OptionalString(value, "bizStep"), OptionalString(value, "disposition")),
                OptionalString(value, "bizStep"), OptionalString(value, "disposition")),
            "AggregationEvent" => new EpcisInboundAggregationEvent(
                eventId, eventTime, readPoint, value.Clone(), RequiredString(value, "parentID"),
                RequiredStrings(value, "childEPCs"), RequiredAction(value)),
            _ => throw new EpcisMappingException("Only ObjectEvent and AggregationEvent are supported.")
        };
    }

    public static string UnitIdentifier(string atlasId, string? gtin, string? serial) =>
        !string.IsNullOrWhiteSpace(gtin) && !string.IsNullOrWhiteSpace(serial)
            ? $"https://id.gs1.org/01/{Uri.EscapeDataString(gtin)}/21/{Uri.EscapeDataString(serial)}"
            : $"urn:unitatlas:unit:{Uri.EscapeDataString(atlasId)}";

    public static string LogisticUnitIdentifier(string code, string? sscc) =>
        !string.IsNullOrWhiteSpace(sscc)
            ? $"https://id.gs1.org/00/{sscc}"
            : $"urn:unitatlas:logistic-unit:{Uri.EscapeDataString(code)}";

    public static string LocationIdentifier(Guid id) => $"urn:unitatlas:location:{id}";

    private static object Map(EpcisTraceSource source)
    {
        var value = Common(source.EventId, source.EventTime, source.RecordTime, "ObjectEvent");
        value["epcList"] = new[] { UnitIdentifier(source.AtlasId, source.Gtin, source.Serial) };
        value["action"] = "OBSERVE";
        value["bizStep"] = BusinessStep(source.EventType, source.BusinessStep);
        Add(value, "disposition", SupportedVocabulary(source.Disposition, KnownDispositions));
        AddLocation(value, "readPoint", source.ReadPoint);
        AddLocation(value, "bizLocation", source.BusinessLocation);
        return value;
    }

    private static object Map(EpcisAggregationSource source)
    {
        var value = Common(source.EventId, source.EventTime, source.RecordTime, "AggregationEvent");
        value["parentID"] = source.ParentIdentifier;
        value["childEPCs"] = source.ChildIdentifiers;
        value["action"] = source.Action.ToUpperInvariant();
        value["bizStep"] = source.Action.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ? "unpacking" : "packing";
        AddLocation(value, "readPoint", source.ReadPoint);
        AddLocation(value, "bizLocation", source.BusinessLocation);
        return value;
    }

    private static Dictionary<string, object?> Common(Guid id, DateTimeOffset eventTime, DateTimeOffset recordTime, string type) => new()
    {
        ["type"] = type,
        ["eventTime"] = eventTime,
        ["recordTime"] = recordTime,
        ["eventTimeZoneOffset"] = eventTime.ToString("zzz", CultureInfo.InvariantCulture),
        ["eventID"] = $"urn:uuid:{id}"
    };

    private static void Add(Dictionary<string, object?> value, string name, string? item)
    {
        if (!string.IsNullOrWhiteSpace(item)) value[name] = item;
    }

    private static void AddLocation(Dictionary<string, object?> value, string name, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) value[name] = new Dictionary<string, string> { ["id"] = id };
    }

    private static string BusinessStep(string eventType, string? configured)
    {
        var supported = SupportedVocabulary(configured, KnownBusinessSteps);
        return supported ?? eventType.ToUpperInvariant() switch
        {
            "MANUFACTURED" => "commissioning", "QUALITY_PASSED" => "inspecting", "PACKED" => "packing",
            "MOVED_TO_WAREHOUSE" => "storing", "SHIPPED" => "shipping", "RECEIVED" => "receiving", _ => "other"
        };
    }

    private static string? SupportedVocabulary(string? value, IReadOnlySet<string> known)
    {
        var normalized = NormalizeVocabulary(value);
        if (normalized is null) return null;
        return known.Contains(normalized) || Uri.TryCreate(normalized, UriKind.Absolute, out _) ? normalized : null;
    }

    private static string? NormalizeVocabulary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        foreach (var prefix in new[] { "urn:epcglobal:cbv:bizstep-", "urn:epcglobal:cbv:disp-", "https://ref.gs1.org/cbv/BizStep-", "https://ref.gs1.org/cbv/Disp-" })
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return normalized[prefix.Length..].ToLowerInvariant();
        return normalized;
    }

    private static string ToUnitAtlasEventType(string? bizStep, string? disposition)
    {
        var step = NormalizeVocabulary(bizStep);
        if (step is not null) return step.ToLowerInvariant() switch
        {
            "commissioning" or "creating_class_instance" => "MANUFACTURED",
            "inspecting" => "QUALITY_PASSED",
            "packing" => "PACKED",
            "shipping" or "departing" => "SHIPPED",
            "receiving" or "arriving" => "RECEIVED",
            "storing" or "stocking" => "MOVED_TO_WAREHOUSE",
            _ => throw new EpcisMappingException($"Unsupported bizStep: {bizStep}")
        };
        return NormalizeVocabulary(disposition)?.ToLowerInvariant() switch
        {
            "in_transit" => "SHIPPED",
            "available" => "RECEIVED",
            _ => throw new EpcisMappingException("ObjectEvent requires a supported bizStep or disposition.")
        };
    }

    private static string RequiredAction(JsonElement value)
    {
        var action = RequiredString(value, "action").ToUpperInvariant();
        return action is "ADD" or "DELETE" ? action : throw new EpcisMappingException("Aggregation action must be ADD or DELETE.");
    }

    private static string RequiredString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw new EpcisMappingException($"{name} is required.");

    private static string? OptionalString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static DateTimeOffset RequiredDate(JsonElement value, string name) =>
        DateTimeOffset.TryParse(RequiredString(value, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : throw new EpcisMappingException($"{name} must be an ISO-8601 timestamp.");

    private static string[] RequiredStrings(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
            throw new EpcisMappingException($"{name} is required.");
        var items = property.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
        return items.Length > 0 ? items : throw new EpcisMappingException($"{name} must contain at least one identifier.");
    }

    private static string? NestedId(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Object
            ? OptionalString(property, "id")
            : null;
}

public sealed class EpcisMappingException(string message) : Exception(message);
