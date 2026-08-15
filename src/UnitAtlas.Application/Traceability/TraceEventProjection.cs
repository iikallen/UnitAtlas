using UnitAtlas.Domain;

namespace UnitAtlas.Application.Traceability;

public static class TraceEventProjection
{
    private static readonly IReadOnlyDictionary<string, string> Statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["MANUFACTURED"] = "Manufactured",
        ["QUALITY_PASSED"] = "QC passed",
        ["PACKED"] = "Packed",
        ["MOVED_TO_WAREHOUSE"] = "In warehouse",
        ["SHIPPED"] = "Shipped",
        ["RECEIVED"] = "Received"
    };

    public static IEnumerable<string> EventTypes => Statuses.Keys;

    public static bool TryGetStatus(string eventType, out string status) =>
        Statuses.TryGetValue(eventType, out status!);

    public static bool Apply(UnitState state, TraceEvent incoming, string status)
    {
        var isNewer = incoming.OccurredAt > state.CurrentOccurredAt
            || incoming.OccurredAt == state.CurrentOccurredAt && incoming.Sequence > state.CurrentSequence;

        if (!isNewer)
            return false;

        state.Status = status;
        state.Location = incoming.Location;
        state.LastEventId = incoming.Id;
        state.CurrentOccurredAt = incoming.OccurredAt;
        state.CurrentSequence = incoming.Sequence;
        state.UpdatedAt = incoming.RecordedAt;
        return true;
    }
}
