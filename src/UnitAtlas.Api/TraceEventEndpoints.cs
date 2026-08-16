using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnitAtlas.Api.Observability;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Application.Traceability;
using UnitAtlas.Contracts;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api;

internal static class TraceEventEndpoints
{
    internal static Task<IResult> RecordEvent(
        string atlasId,
        EventRequest request,
        UnitAtlasDb db,
        ITenantContext tenantContext,
        ILogger<Program> logger)
        => RecordEventCore(atlasId, request, db, tenantContext, logger, null);

    internal static Task<IResult> RecordCaptureEvent(
        string atlasId,
        EventRequest request,
        UnitAtlasDb db,
        ITenantContext tenantContext,
        ILogger<Program> logger,
        CaptureDeviceContext capture)
        => RecordEventCore(atlasId, request, db, tenantContext, logger, capture);

    private static async Task<IResult> RecordEventCore(
        string atlasId,
        EventRequest request,
        UnitAtlasDb db,
        ITenantContext tenantContext,
        ILogger<Program> logger,
        CaptureDeviceContext? capture)
    {
        using var ingestActivity = Telemetry.Activities.StartActivity("unitatlas.event.ingest");
        ingestActivity?.SetTag("unitatlas.atlas_id", atlasId);
        if (!TraceEventProjection.TryGetStatus(request.EventType ?? "", out var status))
            return Validation("eventType", $"Allowed: {string.Join(", ", TraceEventProjection.EventTypes)}");
        if (string.IsNullOrWhiteSpace(request.Location) || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Validation("event", "Location and idempotencyKey are required.");

        var locationIds = new[] { request.ReadPointId, request.BusinessLocationId }.OfType<Guid>().Distinct().ToArray();
        if (locationIds.Length > 0 && await db.Locations.CountAsync(location => locationIds.Contains(location.Id)) != locationIds.Length)
            return Validation("location", "readPointId and businessLocationId must belong to the active tenant.");

        var unit = await db.Units.SingleOrDefaultAsync(x => x.AtlasId == atlasId);
        if (unit is null) return Problem("UNIT_NOT_FOUND", "Unit not found.", 404);
        var operation = $"unit:{unit.Id}:event";
        var requestHash = EventRequestHash.Compute(atlasId, request);
        var idempotencyKey = request.IdempotencyKey.Trim();
        var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Key == idempotencyKey);
        if (existing is not null) return Replay(existing, operation, requestHash);

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM units WHERE \"Id\" = {unit.Id} FOR UPDATE");
            existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Key == idempotencyKey);
            if (existing is not null)
            {
                await transaction.RollbackAsync();
                return Replay(existing, operation, requestHash);
            }

            var sequence = (await db.TraceEvents.Where(x => x.UnitId == unit.Id).MaxAsync(x => (long?)x.Sequence) ?? 0) + 1;
            var now = DateTimeOffset.UtcNow;
            var trace = new TraceEvent
            {
                Id = Guid.CreateVersion7(), TenantId = unit.TenantId, UnitId = unit.Id,
                EventType = request.EventType!.Trim().ToUpperInvariant(), Location = request.Location!.Trim(),
                IdempotencyKey = idempotencyKey, Actor = request.Actor?.Trim() ?? "operator",
                ActorSubject = tenantContext.UserSubject, SourceSystem = request.SourceSystem?.Trim() ?? "unitatlas",
                OccurredAt = request.OccurredAt ?? now, RecordedAt = now, Sequence = sequence,
                ReadPointId = request.ReadPointId, BusinessLocationId = request.BusinessLocationId,
                DeviceId = capture?.DeviceId, StationId = capture?.StationId,
                BusinessStep = request.BusinessStep?.Trim(), Disposition = request.Disposition?.Trim()
            };
            var state = await db.UnitStates.SingleAsync(x => x.UnitId == unit.Id);
            TraceEventProjection.Apply(state, trace, status);
            db.AddRange(trace,
                new IdempotencyRecord
                {
                    Id = Guid.CreateVersion7(), TenantId = unit.TenantId, Key = idempotencyKey,
                    Operation = operation, RequestHash = requestHash, ResourceId = trace.Id,
                    ResponseStatus = 201, CreatedAt = now, ExpiresAt = now.AddHours(24)
                },
                new AuditEntry
                {
                    Id = Guid.CreateVersion7(), TenantId = unit.TenantId, ActorSubject = tenantContext.UserSubject,
                    Action = "trace_event.recorded", EntityType = "TraceEvent", EntityId = trace.Id,
                    DataJson = JsonSerializer.Serialize(new { unit.AtlasId, trace.EventType, trace.OccurredAt }), CreatedAt = now
                },
                new OutboxMessage
                {
                    Id = Guid.CreateVersion7(), TenantId = unit.TenantId, CorrelationId = trace.Id,
                    Source = "unitatlas", Type = "trace_event.recorded", SubjectType = "TraceEvent", SubjectId = trace.Id.ToString(),
                    PayloadJson = JsonSerializer.Serialize(new { trace.Id, unit.AtlasId, state.Status, state.Location }), CreatedAt = now
                });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            ingestActivity?.SetTag("unitatlas.event_id", trace.Id);
            ingestActivity?.SetTag("unitatlas.event_type", trace.EventType);
            Telemetry.EventsRecorded.Add(1, new KeyValuePair<string, object?>("event.type", trace.EventType));
            logger.LogInformation("Trace event recorded {EventId} for {AtlasId}", trace.Id, unit.AtlasId);
            return Results.Created($"/api/v1/units/{atlasId}", new { trace.Id, duplicate = false });
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            var winner = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Key == idempotencyKey);
            if (winner is null) throw;
            return Replay(winner, operation, requestHash);
        }
    }

    private static IResult Replay(IdempotencyRecord record, string operation, string requestHash) =>
        record.Operation == operation && record.RequestHash == requestHash
            ? Results.Json(new { id = record.ResourceId, duplicate = true }, statusCode: record.ResponseStatus)
            : Problem("IDEMPOTENCY_KEY_REUSED", "Idempotency key reused.", 409);
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
    private static IResult Problem(string code, string title, int status) => Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
