using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace UnitAtlas.Api.Observability;

internal static class Telemetry
{
    public const string Name = "UnitAtlas.Api";
    public static readonly ActivitySource Activities = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> EventsRecorded = Meter.CreateCounter<long>("unitatlas.events.recorded");
    public static readonly Counter<long> Exceptions = Meter.CreateCounter<long>("unitatlas.exceptions");
    public static readonly Counter<long> InboxDuplicates = Meter.CreateCounter<long>("integration.inbox.duplicates");
    public static readonly Counter<long> EpcisValidationFailures = Meter.CreateCounter<long>("epcis.validation.failures");
}
