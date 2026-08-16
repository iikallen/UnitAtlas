using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnitAtlas.Application.Packaging;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Contracts;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api;

public static class PackagingEndpoints
{
    public static WebApplication MapPackagingEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();

        api.MapPost("/logistic-units", CreateLogisticUnit)
            .RequireAuthorization(Permissions.PackagingManage);
        api.MapGet("/logistic-units/{code}", GetLogisticUnit)
            .RequireAuthorization(Permissions.PackagingRead)
            .RequireRateLimiting("unit-lookup");
        api.MapPost("/logistic-units/{code}/aggregations", RecordAggregation)
            .RequireAuthorization(Permissions.PackagingManage)
            .RequireRateLimiting("event-ingest");

        return app;
    }

    private static async Task<IResult> CreateLogisticUnit(
        LogisticUnitRequest request,
        UnitAtlasDb db,
        ITenantContext tenantContext)
    {
        var code = request.Code?.Trim();
        var type = request.Type?.Trim().ToUpperInvariant();
        var sscc = request.Sscc?.Trim();
        if (string.IsNullOrWhiteSpace(code))
            return Validation("code", "Logistic unit code is required.");
        if (!PackagingRules.IsSupportedType(type))
            return Validation("type", $"Allowed: {string.Join(", ", PackagingRules.LogisticUnitTypes)}");
        if (!PackagingRules.IsValidSscc(sscc))
            return Validation("sscc", "SSCC must be an 18-digit GS1 SSCC with a valid check digit.");

        var now = DateTimeOffset.UtcNow;
        var logisticUnit = new LogisticUnit
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantContext.TenantId,
            Code = code,
            Type = type!,
            Sscc = string.IsNullOrWhiteSpace(sscc) ? null : sscc,
            CreatedAt = now
        };
        db.AddRange(
            logisticUnit,
            new AuditEntry
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantContext.TenantId,
                ActorSubject = tenantContext.UserSubject,
                Action = "logistic_unit.created",
                EntityType = "LogisticUnit",
                EntityId = logisticUnit.Id,
                DataJson = JsonSerializer.Serialize(new { logisticUnit.Code, logisticUnit.Type, logisticUnit.Sscc }),
                CreatedAt = now
            },
            new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantContext.TenantId,
                CorrelationId = logisticUnit.Id,
                Source = "unitatlas",
                Type = "logistic_unit.created",
                SubjectType = "LogisticUnit",
                SubjectId = logisticUnit.Id.ToString(),
                PayloadJson = JsonSerializer.Serialize(new { logisticUnit.Id, logisticUnit.Code, logisticUnit.Type, logisticUnit.Sscc }),
                CreatedAt = now
            });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem("LOGISTIC_UNIT_EXISTS", "Logistic unit already exists.", StatusCodes.Status409Conflict);
        }

        return Results.Created($"/api/v1/logistic-units/{Uri.EscapeDataString(logisticUnit.Code)}",
            new { logisticUnit.Id, logisticUnit.Code, logisticUnit.Type, logisticUnit.Sscc });
    }

    private static async Task<IResult> GetLogisticUnit(string code, UnitAtlasDb db)
    {
        var parent = await db.LogisticUnits.AsNoTracking().SingleOrDefaultAsync(x => x.Code == code);
        if (parent is null)
            return Problem("LOGISTIC_UNIT_NOT_FOUND", "Logistic unit not found.", StatusCodes.Status404NotFound);

        var unitChildren = await (
            from content in db.LogisticUnitContents.AsNoTracking()
            join unit in db.Units.AsNoTracking() on content.ChildUnitId equals unit.Id
            join product in db.Products.AsNoTracking() on unit.ProductId equals product.Id
            where content.ParentLogisticUnitId == parent.Id && content.ChildUnitId != null
            orderby unit.AtlasId
            select new LogisticUnitChildResponse("UNIT", unit.AtlasId, product.Name, unit.Serial))
            .ToListAsync();
        var logisticChildren = await (
            from content in db.LogisticUnitContents.AsNoTracking()
            join child in db.LogisticUnits.AsNoTracking() on content.ChildLogisticUnitId equals child.Id
            where content.ParentLogisticUnitId == parent.Id && content.ChildLogisticUnitId != null
            orderby child.Code
            select new LogisticUnitChildResponse(child.Type, child.Code, null, null))
            .ToListAsync();
        var eventRows = await db.AggregationEvents.AsNoTracking()
            .Where(x => x.ParentLogisticUnitId == parent.Id)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Sequence)
            .ToListAsync();
        var events = eventRows.Select(x =>
        {
            var children = JsonSerializer.Deserialize<AggregationChildren>(x.ChildrenJson)
                ?? new AggregationChildren([], []);
            return new AggregationEventResponse(x.Id, x.Action, x.OccurredAt, x.RecordedAt, x.Sequence,
                x.ActorSubject, x.SourceSystem, x.ReadPointId, x.BusinessLocationId,
                children.units, children.logisticUnits);
        }).ToArray();

        return Results.Ok(new LogisticUnitContentResponse(parent.Code, parent.Type, parent.Sscc,
            unitChildren.Concat(logisticChildren).ToArray(), events));
    }

    internal static async Task<IResult> RecordAggregation(
        string code,
        AggregationRequest request,
        UnitAtlasDb db,
        ITenantContext tenantContext)
    {
        var action = request.Action?.Trim().ToUpperInvariant();
        var idempotencyKey = request.IdempotencyKey?.Trim();
        var unitCodes = PackagingRules.NormalizeCodes(request.UnitAtlasIds);
        var logisticCodes = PackagingRules.NormalizeCodes(request.LogisticUnitCodes);

        if (!PackagingRules.IsSupportedAction(action))
            return Validation("action", $"Allowed: {string.Join(", ", PackagingRules.AggregationActions)}");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Validation("idempotencyKey", "Idempotency key is required.");
        if (unitCodes.Length + logisticCodes.Length == 0)
            return Validation("children", "At least one unitAtlasId or logisticUnitCode is required.");
        if (logisticCodes.Contains(code, StringComparer.Ordinal))
            return Problem("AGGREGATION_CYCLE", "A logistic unit cannot contain itself.", StatusCodes.Status409Conflict);

        var locationIds = new[] { request.ReadPointId, request.BusinessLocationId }.OfType<Guid>().Distinct().ToArray();
        if (locationIds.Length > 0 && await db.Locations.CountAsync(x => locationIds.Contains(x.Id)) != locationIds.Length)
            return Validation("location", "readPointId and businessLocationId must belong to the active tenant.");

        var parent = await db.LogisticUnits.SingleOrDefaultAsync(x => x.Code == code);
        if (parent is null)
            return Problem("LOGISTIC_UNIT_NOT_FOUND", "Logistic unit not found.", StatusCodes.Status404NotFound);

        var operation = $"logistic-unit:{parent.Id}:aggregation";
        var requestHash = PackagingRules.ComputeRequestHash(code, request);
        var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Key == idempotencyKey);
        if (existing is not null) return IdempotencyResult(existing, operation, requestHash);

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // Graph mutations are serialized per tenant so two concurrent inverse edges cannot both pass cycle detection.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenantContext.TenantId.ToString()}, 0))");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM logistic_units WHERE \"Id\" = {parent.Id} FOR UPDATE");
            existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Key == idempotencyKey);
            if (existing is not null)
            {
                await transaction.RollbackAsync();
                return IdempotencyResult(existing, operation, requestHash);
            }

            var units = await db.Units.Where(x => unitCodes.Contains(x.AtlasId)).ToListAsync();
            if (units.Count != unitCodes.Length)
                return await RollbackProblem(transaction, "AGGREGATION_CHILD_NOT_FOUND", "One or more tracked units were not found.", StatusCodes.Status404NotFound);
            var logistics = await db.LogisticUnits.Where(x => logisticCodes.Contains(x.Code)).ToListAsync();
            if (logistics.Count != logisticCodes.Length)
                return await RollbackProblem(transaction, "AGGREGATION_CHILD_NOT_FOUND", "One or more logistic units were not found.", StatusCodes.Status404NotFound);

            if (action == "ADD" && logistics.Count > 0 && await WouldCreateCycle(parent.Id, logistics.Select(x => x.Id), db))
                return await RollbackProblem(transaction, "AGGREGATION_CYCLE", "Aggregation would create a logistic-unit cycle.", StatusCodes.Status409Conflict);

            var unitIds = units.Select(x => x.Id).ToArray();
            var logisticIds = logistics.Select(x => x.Id).ToArray();
            var memberships = await db.LogisticUnitContents
                .Where(x => (x.ChildUnitId != null && unitIds.Contains(x.ChildUnitId.Value))
                    || (x.ChildLogisticUnitId != null && logisticIds.Contains(x.ChildLogisticUnitId.Value)))
                .ToListAsync();

            if (action == "ADD" && memberships.Count > 0)
                return await RollbackProblem(transaction, "CHILD_ALREADY_AGGREGATED", "One or more children already belong to a logistic unit.", StatusCodes.Status409Conflict);
            if (action == "DELETE" && (memberships.Count != units.Count + logistics.Count || memberships.Any(x => x.ParentLogisticUnitId != parent.Id)))
                return await RollbackProblem(transaction, "CHILD_NOT_IN_PARENT", "One or more children are not direct members of this logistic unit.", StatusCodes.Status409Conflict);

            var sequence = (await db.AggregationEvents.Where(x => x.ParentLogisticUnitId == parent.Id).MaxAsync(x => (long?)x.Sequence) ?? 0) + 1;
            var now = DateTimeOffset.UtcNow;
            var aggregation = new AggregationEvent
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantContext.TenantId,
                ParentLogisticUnitId = parent.Id,
                Action = action!,
                OccurredAt = request.OccurredAt ?? now,
                RecordedAt = now,
                Sequence = sequence,
                ActorSubject = tenantContext.UserSubject,
                SourceSystem = request.SourceSystem?.Trim() ?? "unitatlas",
                IdempotencyKey = idempotencyKey!,
                ReadPointId = request.ReadPointId,
                BusinessLocationId = request.BusinessLocationId,
                CorrelationId = Guid.CreateVersion7(),
                ChildrenJson = JsonSerializer.Serialize(new AggregationChildren(unitCodes, logisticCodes))
            };
            db.AggregationEvents.Add(aggregation);

            if (action == "ADD")
            {
                db.LogisticUnitContents.AddRange(units.Select(unit => new LogisticUnitContent
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantContext.TenantId,
                    ParentLogisticUnitId = parent.Id,
                    ChildUnitId = unit.Id,
                    AddedByEventId = aggregation.Id,
                    CreatedAt = now
                }));
                db.LogisticUnitContents.AddRange(logistics.Select(child => new LogisticUnitContent
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantContext.TenantId,
                    ParentLogisticUnitId = parent.Id,
                    ChildLogisticUnitId = child.Id,
                    AddedByEventId = aggregation.Id,
                    CreatedAt = now
                }));
            }
            else
            {
                db.LogisticUnitContents.RemoveRange(memberships);
            }

            db.AddRange(
                new IdempotencyRecord
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantContext.TenantId,
                    Key = idempotencyKey!,
                    Operation = operation,
                    RequestHash = requestHash,
                    ResourceId = aggregation.Id,
                    ResponseStatus = StatusCodes.Status201Created,
                    CreatedAt = now,
                    ExpiresAt = now.AddHours(24)
                },
                new AuditEntry
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantContext.TenantId,
                    ActorSubject = tenantContext.UserSubject,
                    Action = "aggregation.recorded",
                    EntityType = "AggregationEvent",
                    EntityId = aggregation.Id,
                    DataJson = JsonSerializer.Serialize(new { parent.Code, aggregation.Action, unitCodes, logisticCodes }),
                    CreatedAt = now
                },
                new OutboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantContext.TenantId,
                    CorrelationId = aggregation.Id,
                    Source = "unitatlas",
                    Type = "aggregation.recorded",
                    SubjectType = "AggregationEvent",
                    SubjectId = aggregation.Id.ToString(),
                    PayloadJson = JsonSerializer.Serialize(new { aggregation.Id, parent.Code, aggregation.Action, unitCodes, logisticCodes }),
                    CreatedAt = now
                });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Created($"/api/v1/logistic-units/{Uri.EscapeDataString(parent.Code)}",
                new { id = aggregation.Id, duplicate = false, parent = parent.Code, action = aggregation.Action, children = unitCodes.Length + logisticCodes.Length });
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            var winner = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Key == idempotencyKey);
            return winner is not null
                ? IdempotencyResult(winner, operation, requestHash)
                : Problem("CHILD_ALREADY_AGGREGATED", "A child was concurrently assigned to another logistic unit.", StatusCodes.Status409Conflict);
        }
    }

    private static async Task<bool> WouldCreateCycle(Guid parentId, IEnumerable<Guid> childIds, UnitAtlasDb db)
    {
        var edges = await db.LogisticUnitContents.AsNoTracking()
            .Where(x => x.ChildLogisticUnitId != null)
            .Select(x => new { x.ParentLogisticUnitId, Child = x.ChildLogisticUnitId!.Value })
            .ToListAsync();
        var childrenByParent = edges.GroupBy(x => x.ParentLogisticUnitId)
            .ToDictionary(x => x.Key, x => x.Select(edge => edge.Child).ToArray());
        foreach (var childId in childIds)
        {
            var pending = new Stack<Guid>();
            var visited = new HashSet<Guid>();
            pending.Push(childId);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current)) continue;
                if (current == parentId) return true;
                if (childrenByParent.TryGetValue(current, out var children))
                    foreach (var child in children) pending.Push(child);
            }
        }
        return false;
    }

    private static IResult Validation(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    private static IResult Problem(string code, string title, int status) =>
        Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult IdempotencyResult(IdempotencyRecord record, string operation, string requestHash) =>
        record.Operation == operation && record.RequestHash == requestHash
            ? Results.Json(new { id = record.ResourceId, duplicate = true }, statusCode: record.ResponseStatus)
            : Problem("IDEMPOTENCY_KEY_REUSED", "Idempotency key reused.", StatusCodes.Status409Conflict);

    private static async Task<IResult> RollbackProblem(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string code,
        string title,
        int status)
    {
        await transaction.RollbackAsync();
        return Problem(code, title, status);
    }

    private sealed record AggregationChildren(string[] units, string[] logisticUnits);
}
