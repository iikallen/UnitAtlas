using System.Diagnostics.Metrics;

namespace UnitAtlas.Infrastructure.Integrations;

internal static class IntegrationTelemetry
{
    private static readonly Meter Meter = new("UnitAtlas.Api");
    public static readonly Histogram<double> DeliveryLag = Meter.CreateHistogram<double>("integration.delivery.lag", "s");
    public static readonly Counter<long> Attempts = Meter.CreateCounter<long>("integration.delivery.attempts");
    public static readonly Counter<long> Failures = Meter.CreateCounter<long>("integration.delivery.failures");
    public static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>("integration.delivery.deadletters");
}
