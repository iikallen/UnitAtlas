using Microsoft.EntityFrameworkCore;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Contracts;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api;

public static class CaptureEndpoints
{
    public static WebApplication MapCaptureEndpoints(this WebApplication app)
    {
        var capture = app.MapGroup("/api/v1/capture").RequireAuthorization();
        capture.MapGet("/bootstrap", Bootstrap).RequireAuthorization(Permissions.UnitsRead);
        capture.MapPost("/resolve", Resolve).RequireAuthorization(Permissions.UnitsRead).RequireRateLimiting("capture-sync");
        capture.MapPost("/sync", Sync).RequireAuthorization(Permissions.CaptureUse).RequireRateLimiting("capture-sync");
        capture.MapPost("/quality", Quality).RequireAuthorization(Permissions.EventsRecord).RequireRateLimiting("capture-sync");
        capture.MapPost("/move", Move).RequireAuthorization(Permissions.EventsRecord).RequireRateLimiting("capture-sync");
        return app;
    }

    private static async Task<IResult> Bootstrap(UnitAtlasDb db, ITenantContext tenant)
    {
        var tenantName = await db.Tenants.Where(x => x.Id == tenant.TenantId).Select(x => x.Name).SingleAsync();
        return Results.Ok(new
        {
            device = (object?)null,
            tenant = new { id = tenant.TenantId, name = tenantName },
            sites = await db.Sites.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(),
            locations = await db.Locations.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.SiteId, x.Code, x.Name, x.Type }).ToListAsync(),
            products = await db.Products.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Sku, x.Name, x.Gtin }).ToListAsync(),
            permissions = tenant.GrantedPermissions,
            syncToken = "0"
        });
    }

    private static async Task<IResult> Resolve(CaptureResolveRequest request, UnitAtlasDb db)
    {
        var code = request.Code?.Trim();
        if (string.IsNullOrWhiteSpace(code)) return Validation("code", "Scan code is required.");
        var unit = await db.Units.AsNoTracking().Include(x => x.Product).SingleOrDefaultAsync(x => x.AtlasId == code || x.Serial == code);
        if (unit is not null)
        {
            var state = await db.UnitStates.AsNoTracking().SingleAsync(x => x.UnitId == unit.Id);
            var parent = await (from content in db.LogisticUnitContents.AsNoTracking()
                                join logistic in db.LogisticUnits.AsNoTracking() on content.ParentLogisticUnitId equals logistic.Id
                                where content.ChildUnitId == unit.Id select logistic.Code).SingleOrDefaultAsync();
            return Results.Ok(new { kind = "UNIT", code = unit.AtlasId, unit.Serial, product = unit.Product.Name, state.Status, serverParent = parent });
        }
        var logisticUnit = await db.LogisticUnits.AsNoTracking().SingleOrDefaultAsync(x => x.Code == code || x.Sscc == code);
        if (logisticUnit is not null)
        {
            var parent = await (from content in db.LogisticUnitContents.AsNoTracking()
                                join logistic in db.LogisticUnits.AsNoTracking() on content.ParentLogisticUnitId equals logistic.Id
                                where content.ChildLogisticUnitId == logisticUnit.Id select logistic.Code).SingleOrDefaultAsync();
            return Results.Ok(new { kind = logisticUnit.Type, code = logisticUnit.Code, logisticUnit.Sscc, serverParent = parent });
        }
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Gtin == code || x.Sku == code);
        if (product is not null)
            return Results.Ok(new { kind = "PRODUCT", code = product.Sku, product.Name, product.Gtin, serverParent = (string?)null });
        return Problem("IDENTIFIER_NOT_FOUND", "No UnitAtlas object matches this identifier.", 404);
    }

    private static Task<IResult> Sync(
        CaptureSyncRequest request,
        UnitAtlasDb db,
        ITenantContext tenant,
        ILogger<Program> logger)
    {
        if (request.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(request.DeviceId))
            return Task.FromResult(Validation("command", "commandId and deviceId are required."));
        var key = $"capture:{request.DeviceId.Trim()}:{request.CommandId:N}";
        if (string.Equals(request.CommandType?.Trim(), "AGGREGATE", StringComparison.OrdinalIgnoreCase))
        {
            if (!tenant.GrantedPermissions.Contains(Permissions.PackagingManage)) return Task.FromResult(Results.Forbid());
            if (string.IsNullOrWhiteSpace(request.ParentCode)) return Task.FromResult(Validation("parentCode", "parentCode is required."));
            return PackagingEndpoints.RecordAggregation(request.ParentCode.Trim(), new AggregationRequest(
                request.Action, key, request.UnitAtlasIds, request.LogisticUnitCodes, request.OccurredAt,
                request.ReadPointId, request.BusinessLocationId, "unitatlas-capture"), db, tenant);
        }
        if (string.Equals(request.CommandType?.Trim(), "TRACE_EVENT", StringComparison.OrdinalIgnoreCase))
        {
            if (!tenant.GrantedPermissions.Contains(Permissions.EventsRecord)) return Task.FromResult(Results.Forbid());
            if (string.IsNullOrWhiteSpace(request.UnitAtlasId)) return Task.FromResult(Validation("unitAtlasId", "unitAtlasId is required."));
            return TraceEventEndpoints.RecordEvent(request.UnitAtlasId.Trim(), new EventRequest(
                request.EventType, request.Location, key, null, request.OccurredAt, request.ReadPointId,
                request.BusinessLocationId, null, null, "unitatlas-capture"), db, tenant, logger);
        }
        return Task.FromResult(Validation("commandType", "Allowed: AGGREGATE, TRACE_EVENT."));
    }

    private static Task<IResult> Quality(
        CaptureQualityRequest request,
        UnitAtlasDb db,
        ITenantContext tenant,
        ILogger<Program> logger)
    {
        var eventType = request.Outcome?.Trim().ToUpperInvariant() switch
        {
            "PASS" => "QUALITY_PASSED",
            "FAIL" => "QUALITY_FAILED",
            "HOLD" => "QUALITY_HOLD",
            _ => null
        };
        if (request.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(request.DeviceId)
            || string.IsNullOrWhiteSpace(request.UnitAtlasId) || eventType is null || string.IsNullOrWhiteSpace(request.Location))
            return Task.FromResult(Validation("quality", "commandId, deviceId, unitAtlasId, location and outcome PASS, FAIL or HOLD are required."));
        return TraceEventEndpoints.RecordEvent(request.UnitAtlasId.Trim(), new EventRequest(
            eventType, request.Location, $"capture:{request.DeviceId.Trim()}:{request.CommandId:N}", null,
            request.OccurredAt, request.ReadPointId, request.BusinessLocationId, "quality_inspection", null,
            "unitatlas-capture"), db, tenant, logger);
    }

    private static Task<IResult> Move(
        CaptureMoveRequest request,
        UnitAtlasDb db,
        ITenantContext tenant,
        ILogger<Program> logger)
    {
        if (request.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(request.DeviceId)
            || string.IsNullOrWhiteSpace(request.UnitAtlasId) || string.IsNullOrWhiteSpace(request.To))
            return Task.FromResult(Validation("move", "commandId, deviceId, unitAtlasId and destination are required."));
        return TraceEventEndpoints.RecordEvent(request.UnitAtlasId.Trim(), new EventRequest(
            "MOVED_TO_WAREHOUSE", request.To, $"capture:{request.DeviceId.Trim()}:{request.CommandId:N}", null,
            request.OccurredAt, request.ReadPointId, request.BusinessLocationId, "storing", null,
            "unitatlas-capture"), db, tenant, logger);
    }

    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
    private static IResult Problem(string code, string title, int status) => Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
