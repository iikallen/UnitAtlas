using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnitAtlas.Api.Observability;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Application.Traceability;
using UnitAtlas.Contracts;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure.Integrations.Epcis;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api;

public static class EpcisEndpoints
{
    public static WebApplication MapEpcisEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/epcis/documents").RequireAuthorization();
        api.MapGet("/", ExportAsync).RequireAuthorization(Permissions.IntegrationsRead);
        api.MapPost("/", ImportAsync).RequireAuthorization(Permissions.IntegrationsManage);
        return app;
    }

    private static async Task<IResult> ExportAsync(UnitAtlasDb db)
    {
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(x => x.Id, x => EpcisMapper.LocationIdentifier(x.Id));
        var units = await db.Units.AsNoTracking().Include(x => x.Product).ToDictionaryAsync(
            x => x.AtlasId, x => EpcisMapper.UnitIdentifier(x.AtlasId, x.Product.Gtin, x.Serial));
        var logistics = await db.LogisticUnits.AsNoTracking().ToDictionaryAsync(
            x => x.Code, x => EpcisMapper.LogisticUnitIdentifier(x.Code, x.Sscc));

        var traceRows = await (from trace in db.TraceEvents.AsNoTracking()
            join unit in db.Units.AsNoTracking() on trace.UnitId equals unit.Id
            join product in db.Products.AsNoTracking() on unit.ProductId equals product.Id
            orderby trace.OccurredAt, trace.Sequence
            select new { Trace = trace, Unit = unit, Product = product }).ToListAsync();
        var traces = traceRows.Select(x => new EpcisTraceSource(
            x.Trace.Id, x.Trace.OccurredAt, x.Trace.RecordedAt, x.Trace.EventType,
            x.Trace.BusinessStep, x.Trace.Disposition,
            Location(locations, x.Trace.ReadPointId), Location(locations, x.Trace.BusinessLocationId),
            x.Unit.AtlasId, x.Product.Gtin, x.Unit.Serial));

        var aggregationRows = await db.AggregationEvents.AsNoTracking().OrderBy(x => x.OccurredAt).ThenBy(x => x.Sequence).ToListAsync();
        var parentIdentifiers = await db.LogisticUnits.AsNoTracking().ToDictionaryAsync(x => x.Id,
            x => EpcisMapper.LogisticUnitIdentifier(x.Code, x.Sscc));
        var aggregations = aggregationRows.Select(row =>
        {
            var children = JsonSerializer.Deserialize<AggregationChildren>(row.ChildrenJson) ?? new([], []);
            var identifiers = children.units.Select(code => units[code]).Concat(children.logisticUnits.Select(code => logistics[code])).ToArray();
            return new EpcisAggregationSource(row.Id, row.OccurredAt, row.RecordedAt, row.Action,
                parentIdentifiers[row.ParentLogisticUnitId], identifiers,
                Location(locations, row.ReadPointId), Location(locations, row.BusinessLocationId));
        });
        return Results.Json(EpcisMapper.Export(traces, aggregations, DateTimeOffset.UtcNow), contentType: "application/ld+json");
    }

    private static async Task<IResult> ImportAsync(HttpRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted); }
        catch (JsonException) { return Problem("EPCIS_INVALID_JSON", "Body must be valid JSON.", 400); }

        using (document)
        {
            EpcisInboundEvent inbound;
            try { inbound = EpcisMapper.Import(document.RootElement); }
            catch (EpcisMappingException exception) { return Problem("EPCIS_UNSUPPORTED_DOCUMENT", exception.Message, 422); }

            var source = request.Headers["X-EPCIS-Source"].FirstOrDefault()?.Trim() ?? "epcis";
            if (source.Length is < 1 or > 80) return Problem("EPCIS_INVALID_SOURCE", "X-EPCIS-Source must be 1-80 characters.", 400);
            return inbound switch
            {
                EpcisInboundObjectEvent value => await CaptureObjectAsync(value, source, db, tenant),
                EpcisInboundAggregationEvent value => await CaptureAggregationAsync(value, source, db, tenant),
                _ => Problem("EPCIS_UNSUPPORTED_DOCUMENT", "Unsupported EPCIS event.", 422)
            };
        }
    }

    private static async Task<IResult> CaptureObjectAsync(
        EpcisInboundObjectEvent inbound, string source, UnitAtlasDb db, ITenantContext tenant)
    {
        var resolved = new List<(string Epc, TrackedUnit Unit)>();
        foreach (var epc in inbound.Epcs.Distinct(StringComparer.Ordinal))
        {
            var unit = await ResolveUnitAsync(epc, db);
            if (unit is null) return Problem("EPCIS_IDENTIFIER_NOT_FOUND", $"Tracked unit not found for {epc}.", 404);
            resolved.Add((epc, unit));
        }

        var raw = inbound.Raw.GetRawText();
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        var keys = resolved.ToDictionary(x => x.Unit.Id, x => $"epcis:{source}:{inbound.EventId}:{x.Epc}");
        var existing = await db.IdempotencyRecords.AsNoTracking().Where(x => keys.Values.Contains(x.Key)).ToListAsync();
        if (existing.Any(x => x.RequestHash != requestHash))
            return Problem("EPCIS_IDEMPOTENCY_CONFLICT", "eventID was already captured with a different payload.", 409);
        if (existing.Count == resolved.Count)
            return Results.Ok(new { ids = existing.Select(x => x.ResourceId), duplicate = true });

        Guid? readPointId = ParseLocation(inbound.ReadPoint);
        if (readPointId is not null && !await db.Locations.AnyAsync(x => x.Id == readPointId))
            return Problem("EPCIS_LOCATION_NOT_FOUND", "readPoint does not belong to this tenant.", 404);

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.TenantId.ToString()}, 1))");
        foreach (var unitId in resolved.Select(x => x.Unit.Id).Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM units WHERE \"Id\" = {unitId} FOR UPDATE");
        existing = await db.IdempotencyRecords.AsNoTracking().Where(x => keys.Values.Contains(x.Key)).ToListAsync();
        if (existing.Any(x => x.RequestHash != requestHash))
        {
            await transaction.RollbackAsync();
            return Problem("EPCIS_IDEMPOTENCY_CONFLICT", "eventID was already captured with a different payload.", 409);
        }
        if (existing.Count == resolved.Count)
        {
            await transaction.RollbackAsync();
            return Results.Ok(new { ids = existing.Select(x => x.ResourceId), duplicate = true });
        }
        var newEvents = new List<TraceEvent>();
        foreach (var (epc, unit) in resolved.Where(x => existing.All(item => item.Key != keys[x.Unit.Id])))
        {
            var sequence = (await db.TraceEvents.Where(x => x.UnitId == unit.Id).MaxAsync(x => (long?)x.Sequence) ?? 0) + 1;
            var now = DateTimeOffset.UtcNow;
            var trace = new TraceEvent
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, UnitId = unit.Id, EventType = inbound.EventType,
                OccurredAt = inbound.EventTime, RecordedAt = now, Sequence = sequence,
                Location = inbound.ReadPoint ?? "EPCIS", Actor = tenant.UserSubject, ActorSubject = tenant.UserSubject,
                SourceSystem = source, IdempotencyKey = keys[unit.Id], BusinessStep = inbound.BusinessStep,
                Disposition = inbound.Disposition, ReadPointId = readPointId, CorrelationId = ParseEventId(inbound.EventId),
                MetadataJson = JsonSerializer.Serialize(new { epcisEventId = inbound.EventId, epc })
            };
            var state = await db.UnitStates.SingleAsync(x => x.UnitId == unit.Id);
            TraceEventProjection.TryGetStatus(trace.EventType, out var status);
            TraceEventProjection.Apply(state, trace, status);
            var outboxId = Guid.CreateVersion7();
            db.AddRange(trace,
                new IdempotencyRecord { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, Key = trace.IdempotencyKey,
                    Operation = $"unit:{unit.Id}:event", RequestHash = requestHash, ResourceId = trace.Id,
                    ResponseStatus = 201, CreatedAt = now, ExpiresAt = now.AddDays(7) },
                new AuditEntry { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ActorSubject = tenant.UserSubject,
                    Action = "epcis.object_event.captured", EntityType = "TraceEvent", EntityId = trace.Id,
                    DataJson = JsonSerializer.Serialize(new { inbound.EventId, unit.AtlasId }), CreatedAt = now },
                new OutboxMessage { Id = outboxId, TenantId = tenant.TenantId, CorrelationId = trace.CorrelationId ?? trace.Id,
                    Source = source, Type = "trace_event.recorded", SubjectType = "TraceEvent", SubjectId = trace.Id.ToString(),
                    PayloadJson = JsonSerializer.Serialize(new { trace.Id, unit.AtlasId }), CreatedAt = now });
            newEvents.Add(trace);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Created("/api/v1/epcis/documents", new { ids = newEvents.Select(x => x.Id), duplicate = false });
    }

    private static async Task<IResult> CaptureAggregationAsync(
        EpcisInboundAggregationEvent inbound, string source, UnitAtlasDb db, ITenantContext tenant)
    {
        var parent = await ResolveLogisticUnitAsync(inbound.Parent, db);
        if (parent is null) return Problem("EPCIS_IDENTIFIER_NOT_FOUND", "Parent logistic unit was not found.", 404);
        var unitCodes = new List<string>();
        var logisticCodes = new List<string>();
        foreach (var child in inbound.Children.Distinct(StringComparer.Ordinal))
        {
            var unit = await ResolveUnitAsync(child, db);
            if (unit is not null) { unitCodes.Add(unit.AtlasId); continue; }
            var logistic = await ResolveLogisticUnitAsync(child, db);
            if (logistic is null) return Problem("EPCIS_IDENTIFIER_NOT_FOUND", $"Child not found for {child}.", 404);
            logisticCodes.Add(logistic.Code);
        }
        var readPointId = ParseLocation(inbound.ReadPoint);
        return await PackagingEndpoints.RecordAggregation(parent.Code, new AggregationRequest(
            inbound.Action, $"epcis:{source}:{inbound.EventId}", unitCodes, logisticCodes,
            inbound.EventTime, readPointId, null, source), db, tenant);
    }

    private static async Task<TrackedUnit?> ResolveUnitAsync(string identifier, UnitAtlasDb db)
    {
        const string internalPrefix = "urn:unitatlas:unit:";
        if (identifier.StartsWith(internalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var atlasId = Uri.UnescapeDataString(identifier[internalPrefix.Length..]);
            return await db.Units.SingleOrDefaultAsync(x => x.AtlasId == atlasId);
        }
        if (TryDigitalLink(identifier, "01", "21", out var gtin, out var serial))
            return await db.Units.Include(x => x.Product).SingleOrDefaultAsync(x => x.Product.Gtin == gtin && x.Serial == serial);
        return null;
    }

    private static async Task<LogisticUnit?> ResolveLogisticUnitAsync(string identifier, UnitAtlasDb db)
    {
        const string internalPrefix = "urn:unitatlas:logistic-unit:";
        if (identifier.StartsWith(internalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = Uri.UnescapeDataString(identifier[internalPrefix.Length..]);
            return await db.LogisticUnits.SingleOrDefaultAsync(x => x.Code == code);
        }
        if (TryDigitalLink(identifier, "00", null, out var sscc, out _))
            return await db.LogisticUnits.SingleOrDefaultAsync(x => x.Sscc == sscc);
        return null;
    }

    private static bool TryDigitalLink(string identifier, string firstAi, string? secondAi, out string first, out string second)
    {
        first = second = "";
        if (!Uri.TryCreate(identifier, UriKind.Absolute, out var uri) || !uri.Host.Equals("id.gs1.org", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length < 2 || parts[0] != firstAi) return false;
        first = parts[1];
        if (secondAi is null) return true;
        if (parts.Length < 4 || parts[2] != secondAi) return false;
        second = parts[3];
        return true;
    }

    private static Guid? ParseLocation(string? value)
    {
        const string prefix = "urn:unitatlas:location:";
        return value?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true && Guid.TryParse(value[prefix.Length..], out var id) ? id : null;
    }

    private static Guid? ParseEventId(string value)
    {
        const string prefix = "urn:uuid:";
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value[prefix.Length..], out var id) ? id : null;
    }

    private static string? Location(IReadOnlyDictionary<Guid, string> locations, Guid? id) =>
        id is not null && locations.TryGetValue(id.Value, out var value) ? value : null;

    private static IResult Problem(string code, string detail, int status)
    {
        if (status is 400 or 422) Telemetry.EpcisValidationFailures.Add(1,
            new KeyValuePair<string, object?>("error.code", code));
        return Results.Problem(statusCode: status, title: code, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }

    private sealed record AggregationChildren(string[] units, string[] logisticUnits);
}
