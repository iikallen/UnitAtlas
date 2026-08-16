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
        capture.MapPost("/sync", Sync).RequireAuthorization(Permissions.PackagingManage).RequireRateLimiting("capture-sync");
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
        return Problem("IDENTIFIER_NOT_FOUND", "No UnitAtlas object matches this identifier.", 404);
    }

    private static Task<IResult> Sync(CaptureSyncRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        if (request.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(request.DeviceId)
            || !string.Equals(request.CommandType?.Trim(), "AGGREGATE", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(request.ParentCode))
            return Task.FromResult(Validation("command", "commandId, deviceId, commandType AGGREGATE and parentCode are required."));
        var aggregation = new AggregationRequest(
            request.Action,
            $"capture:{request.DeviceId.Trim()}:{request.CommandId:N}",
            request.UnitAtlasIds,
            request.LogisticUnitCodes,
            request.OccurredAt,
            request.ReadPointId,
            request.BusinessLocationId,
            "unitatlas-capture");
        return PackagingEndpoints.RecordAggregation(request.ParentCode.Trim(), aggregation, db, tenant);
    }

    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
    private static IResult Problem(string code, string title, int status) => Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
