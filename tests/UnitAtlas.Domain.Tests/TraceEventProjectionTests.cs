using UnitAtlas.Application.Traceability;
using UnitAtlas.Domain;

namespace UnitAtlas.Domain.Tests;

public sealed class TraceEventProjectionTests
{
    [Fact]
    public void Older_event_does_not_roll_back_current_state()
    {
        var shippedAt = DateTimeOffset.Parse("2026-08-16T11:00:00Z");
        var shippedEventId = Guid.NewGuid();
        var state = new UnitState
        {
            UnitId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Status = "Shipped",
            Location = "Dock",
            LastEventId = shippedEventId,
            CurrentOccurredAt = shippedAt,
            CurrentSequence = 3,
            UpdatedAt = shippedAt
        };
        var delayedQualityEvent = EventAt(shippedAt.AddHours(-1), 4, "QUALITY_PASSED", "QC Station");

        var applied = TraceEventProjection.Apply(state, delayedQualityEvent, "QC passed");

        Assert.False(applied);
        Assert.Equal("Shipped", state.Status);
        Assert.Equal("Dock", state.Location);
        Assert.Equal(shippedEventId, state.LastEventId);
    }

    [Fact]
    public void Higher_sequence_breaks_equal_timestamp_tie()
    {
        var occurredAt = DateTimeOffset.Parse("2026-08-16T11:00:00Z");
        var state = new UnitState
        {
            UnitId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Status = "Packed",
            Location = "Line 3",
            LastEventId = Guid.NewGuid(),
            CurrentOccurredAt = occurredAt,
            CurrentSequence = 3,
            UpdatedAt = occurredAt
        };
        var shipped = EventAt(occurredAt, 4, "SHIPPED", "Dock");

        Assert.True(TraceEventProjection.Apply(state, shipped, "Shipped"));
        Assert.Equal("Shipped", state.Status);
        Assert.Equal(4, state.CurrentSequence);
    }

    private static TraceEvent EventAt(DateTimeOffset occurredAt, long sequence, string type, string location) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        UnitId = Guid.NewGuid(),
        EventType = type,
        OccurredAt = occurredAt,
        RecordedAt = occurredAt.AddMinutes(1),
        Sequence = sequence,
        Location = location,
        Actor = "test",
        SourceSystem = "test",
        IdempotencyKey = Guid.NewGuid().ToString()
    };
}
