using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Contracts;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api;

public static class CaptureEndpoints
{
    public static WebApplication MapCaptureEndpoints(this WebApplication app)
    {
        var enrollment = app.MapGroup("/api/v1/capture").RequireAuthorization();
        enrollment.MapPost("/enroll", Enroll)
            .RequireAuthorization(Permissions.CaptureUse)
            .RequireRateLimiting("capture-sync");

        var capture = app.MapGroup("/api/v1/capture")
            .RequireAuthorization()
            .AddEndpointFilter<CaptureSessionFilter>();
        capture.MapGet("/bootstrap", Bootstrap).RequireAuthorization(Permissions.CaptureUse);
        capture.MapGet("/changes", Changes).RequireAuthorization(Permissions.CaptureUse).RequireRateLimiting("capture-sync");
        capture.MapPost("/resolve", Resolve).RequireAuthorization(Permissions.UnitsRead).RequireRateLimiting("capture-sync");
        capture.MapPost("/sync", Sync).RequireAuthorization(Permissions.CaptureUse).RequireRateLimiting("capture-sync");
        capture.MapPost("/production", Production).RequireAuthorization(Permissions.EventsRecord).RequireRateLimiting("capture-sync");
        capture.MapPost("/quality", Quality).RequireAuthorization(Permissions.EventsRecord).RequireRateLimiting("capture-sync");
        capture.MapPost("/move", Move).RequireAuthorization(Permissions.EventsRecord).RequireRateLimiting("capture-sync");
        return app;
    }

    private static async Task<IResult> Enroll(CaptureEnrollRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var deviceCode = request.DeviceCode?.Trim().ToUpperInvariant();
        var code = request.EnrollmentCode?.Trim();
        if (string.IsNullOrWhiteSpace(deviceCode) || string.IsNullOrWhiteSpace(code))
            return Validation("enrollment", "deviceCode and enrollmentCode are required.");
        var now = DateTimeOffset.UtcNow;
        var hash = CaptureSecrets.Hash(code);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM device_enrollments WHERE \"EnrollmentCodeHash\" = {hash} FOR UPDATE");
        var enrollment = await db.DeviceEnrollments.SingleOrDefaultAsync(x =>
            x.EnrollmentCodeHash == hash && x.RedeemedAt == null && x.ExpiresAt > now);
        if (enrollment is null)
        {
            await transaction.RollbackAsync();
            return Problem("ENROLLMENT_INVALID", "Enrollment code is invalid, expired or already used.", 401);
        }
        var device = await db.Devices.SingleAsync(x => x.Id == enrollment.DeviceId);
        var station = await db.Stations.SingleAsync(x => x.Id == enrollment.StationId);
        if (!device.IsEnabled || !station.IsEnabled || device.Code != deviceCode)
        {
            await transaction.RollbackAsync();
            return Problem("ENROLLMENT_INVALID", "Enrollment does not match an enabled device and station.", 401);
        }

        var token = CaptureSecrets.CreateToken();
        var session = new DeviceSession
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, DeviceId = device.Id, StationId = station.Id,
            UserSubject = tenant.UserSubject, TokenHash = CaptureSecrets.Hash(token), CreatedAt = now,
            ExpiresAt = now.AddDays(30), LastSeenAt = now
        };
        enrollment.RedeemedAt = now;
        db.AddRange(session, new AuditEntry
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ActorSubject = tenant.UserSubject,
            Action = "device_session.created", EntityType = "DeviceSession", EntityId = session.Id,
            DataJson = JsonSerializer.Serialize(new { session.DeviceId, session.StationId, session.ExpiresAt }), CreatedAt = now
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Created("/api/v1/capture/bootstrap", new
        {
            sessionToken = token,
            session.ExpiresAt,
            device = new { device.Id, device.Code, device.Name, device.Platform },
            station = new { station.Id, station.Code, station.Name, station.SiteId, station.ReadPointId, station.BusinessLocationId }
        });
    }

    private static async Task<IResult> Bootstrap(UnitAtlasDb db, ITenantContext tenant, CaptureDeviceContext capture)
    {
        var tenantName = await db.Tenants.Where(x => x.Id == tenant.TenantId).Select(x => x.Name).SingleAsync();
        var syncToken = await db.OutboxMessages.MaxAsync(x => (long?)x.Sequence) ?? 0;
        return Results.Ok(new
        {
            device = new { id = capture.DeviceId, code = capture.DeviceCode, name = capture.DeviceName },
            station = new
            {
                id = capture.StationId, code = capture.StationCode, name = capture.StationName,
                siteId = capture.SiteId, readPointId = capture.ReadPointId,
                businessLocationId = capture.BusinessLocationId
            },
            sessionExpiresAt = capture.ExpiresAt,
            tenant = new { id = tenant.TenantId, name = tenantName },
            sites = await db.Sites.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(),
            locations = await db.Locations.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.SiteId, x.Code, x.Name, x.Type }).ToListAsync(),
            products = await db.Products.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Sku, x.Name, x.Gtin }).ToListAsync(),
            permissions = tenant.GrantedPermissions,
            syncToken = syncToken.ToString(CultureInfo.InvariantCulture)
        });
    }

    private static async Task<IResult> Changes(string? after, int? limit, UnitAtlasDb db)
    {
        if (!long.TryParse(after ?? "0", NumberStyles.None, CultureInfo.InvariantCulture, out var cursor) || cursor < 0)
            return Validation("after", "after must be a non-negative sync token.");
        var take = Math.Clamp(limit ?? 200, 1, 500);
        var rows = await db.OutboxMessages.AsNoTracking()
            .Where(x => x.Sequence > cursor)
            .OrderBy(x => x.Sequence)
            .Select(x => new ChangeRow(x.Sequence, x.Type, x.SubjectType, x.SubjectId, x.PayloadJson))
            .Take(take + 1)
            .ToListAsync();
        var page = rows.Take(take).ToArray();
        var changes = page.Select(ToChange).ToArray();
        var next = page.Length == 0 ? cursor : page[^1].Sequence;
        return Results.Ok(new CaptureChangesResponse(changes, next.ToString(CultureInfo.InvariantCulture), rows.Count > take));
    }

    private static CaptureChangeResponse ToChange(ChangeRow row)
    {
        using var document = JsonDocument.Parse(row.PayloadJson);
        var payload = document.RootElement.Clone();
        var resourceType = row.Type switch
        {
            "unit.created" or "trace_event.recorded" => "UNIT",
            "logistic_unit.created" or "aggregation.recorded" => "LOGISTIC_UNIT",
            _ => row.SubjectType.ToUpperInvariant()
        };
        var resourceId = Property(payload, "AtlasId", "atlasId")
            ?? Property(payload, "Code", "code")
            ?? row.SubjectId;
        return new CaptureChangeResponse(row.Sequence, resourceType, resourceId, row.Type, payload);
    }

    private static string? Property(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property))
                return property.GetString();
        return null;
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

    private static async Task<IResult> Production(
        CaptureProductionRequest request,
        UnitAtlasDb db,
        ITenantContext tenant,
        CaptureDeviceContext capture,
        ILogger<Program> logger)
    {
        var scannedCode = request.ScannedCode?.Trim();
        if (request.CommandId == Guid.Empty || !MatchesDevice(request.DeviceId, capture)
            || string.IsNullOrWhiteSpace(scannedCode) || string.IsNullOrWhiteSpace(request.Location))
            return Validation("production", "commandId, matching deviceId, scannedCode and location are required.");

        var unit = await db.Units.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AtlasId == scannedCode || x.Serial == scannedCode);
        if (unit is null) return Problem("UNIT_NOT_FOUND", "Scanned unit was not found.", 404);
        var printed = await (from item in db.PrintJobItems.AsNoTracking()
                             join job in db.PrintJobs.AsNoTracking() on item.PrintJobId equals job.Id
                             where item.EntityType == "UNIT" && item.EntityId == unit.Id && job.Status == "PRINTED"
                             select job.Id).AnyAsync();
        if (!printed) return Problem("LABEL_NOT_PRINTED", "The unit has no completed print job.", 409);

        return await TraceEventEndpoints.RecordCaptureEvent(unit.AtlasId, new EventRequest(
            "COMMISSIONED", request.Location.Trim(), $"capture:{capture.DeviceId:N}:{request.CommandId:N}",
            null, request.OccurredAt, capture.ReadPointId, capture.BusinessLocationId,
            "commissioning", null, "unitatlas-capture"), db, tenant, logger, capture);
    }

    private static Task<IResult> Sync(
        CaptureSyncRequest request,
        UnitAtlasDb db,
        ITenantContext tenant,
        CaptureDeviceContext capture,
        ILogger<Program> logger)
    {
        if (request.CommandId == Guid.Empty)
            return Task.FromResult(Validation("command", "commandId is required."));
        if (!MatchesDevice(request.DeviceId, capture)) return Task.FromResult(DeviceMismatch());
        var key = $"capture:{capture.DeviceId:N}:{request.CommandId:N}";
        if (string.Equals(request.CommandType?.Trim(), "AGGREGATE", StringComparison.OrdinalIgnoreCase))
        {
            if (!tenant.GrantedPermissions.Contains(Permissions.PackagingManage)) return Task.FromResult(Results.Forbid());
            if (string.IsNullOrWhiteSpace(request.ParentCode)) return Task.FromResult(Validation("parentCode", "parentCode is required."));
            return PackagingEndpoints.RecordCaptureAggregation(request.ParentCode.Trim(), new AggregationRequest(
                request.Action, key, request.UnitAtlasIds, request.LogisticUnitCodes, request.OccurredAt,
                capture.ReadPointId, capture.BusinessLocationId, "unitatlas-capture"), db, tenant, capture);
        }
        if (string.Equals(request.CommandType?.Trim(), "TRACE_EVENT", StringComparison.OrdinalIgnoreCase))
        {
            if (!tenant.GrantedPermissions.Contains(Permissions.EventsRecord)) return Task.FromResult(Results.Forbid());
            if (string.IsNullOrWhiteSpace(request.UnitAtlasId)) return Task.FromResult(Validation("unitAtlasId", "unitAtlasId is required."));
            return TraceEventEndpoints.RecordCaptureEvent(request.UnitAtlasId.Trim(), new EventRequest(
                request.EventType, request.Location, key, null, request.OccurredAt, capture.ReadPointId,
                capture.BusinessLocationId, null, null, "unitatlas-capture"), db, tenant, logger, capture);
        }
        return Task.FromResult(Validation("commandType", "Allowed: AGGREGATE, TRACE_EVENT."));
    }

    private static Task<IResult> Quality(
        CaptureQualityRequest request,
        UnitAtlasDb db,
        ITenantContext tenant,
        CaptureDeviceContext capture,
        ILogger<Program> logger)
    {
        var eventType = request.Outcome?.Trim().ToUpperInvariant() switch
        {
            "PASS" => "QUALITY_PASSED",
            "FAIL" => "QUALITY_FAILED",
            "HOLD" => "QUALITY_HOLD",
            _ => null
        };
        if (request.CommandId == Guid.Empty || !MatchesDevice(request.DeviceId, capture)
            || string.IsNullOrWhiteSpace(request.UnitAtlasId) || eventType is null || string.IsNullOrWhiteSpace(request.Location))
            return Task.FromResult(Validation("quality", "commandId, matching deviceId, unitAtlasId, location and outcome PASS, FAIL or HOLD are required."));
        return TraceEventEndpoints.RecordCaptureEvent(request.UnitAtlasId.Trim(), new EventRequest(
            eventType, request.Location, $"capture:{capture.DeviceId:N}:{request.CommandId:N}", null,
            request.OccurredAt, capture.ReadPointId, capture.BusinessLocationId, "quality_inspection", null,
            "unitatlas-capture"), db, tenant, logger, capture);
    }

    private static Task<IResult> Move(
        CaptureMoveRequest request,
        UnitAtlasDb db,
        ITenantContext tenant,
        CaptureDeviceContext capture,
        ILogger<Program> logger)
    {
        if (request.CommandId == Guid.Empty || !MatchesDevice(request.DeviceId, capture)
            || string.IsNullOrWhiteSpace(request.UnitAtlasId) || string.IsNullOrWhiteSpace(request.To))
            return Task.FromResult(Validation("move", "commandId, matching deviceId, unitAtlasId and destination are required."));
        return TraceEventEndpoints.RecordCaptureEvent(request.UnitAtlasId.Trim(), new EventRequest(
            "MOVED_TO_WAREHOUSE", request.To, $"capture:{capture.DeviceId:N}:{request.CommandId:N}", null,
            request.OccurredAt, capture.ReadPointId, capture.BusinessLocationId, "storing", null,
            "unitatlas-capture"), db, tenant, logger, capture);
    }

    private static bool MatchesDevice(string? value, CaptureDeviceContext capture) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), capture.DeviceCode, StringComparison.OrdinalIgnoreCase);

    private static IResult DeviceMismatch() => Problem("DEVICE_COMMAND_MISMATCH", "Command device does not match the active device session.", 409);
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
    private static IResult Problem(string code, string title, int status) => Results.Problem(statusCode: status, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record ChangeRow(long Sequence, string Type, string SubjectType, string SubjectId, string PayloadJson);
}
