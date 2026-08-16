using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Contracts;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api;

public static class DeviceEndpoints
{
    public static WebApplication MapDeviceEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(Permissions.TenantManage);
        api.MapGet("/devices", async (UnitAtlasDb db) => Results.Ok(await db.Devices.AsNoTracking().OrderBy(x => x.Code).ToListAsync()));
        api.MapGet("/stations", async (UnitAtlasDb db) => Results.Ok(await db.Stations.AsNoTracking().OrderBy(x => x.Code).ToListAsync()));
        api.MapGet("/device-enrollments", async (UnitAtlasDb db) => Results.Ok(await db.DeviceEnrollments.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.DeviceId, x.StationId, x.CreatedBySubject, x.CreatedAt, x.ExpiresAt, x.RedeemedAt })
            .ToListAsync()));
        api.MapGet("/device-sessions", async (UnitAtlasDb db) => Results.Ok(await db.DeviceSessions.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.DeviceId, x.StationId, x.UserSubject, x.CreatedAt, x.ExpiresAt, x.LastSeenAt, x.RevokedAt })
            .ToListAsync()));
        api.MapPost("/devices", CreateDevice);
        api.MapPost("/stations", CreateStation);
        api.MapPost("/device-enrollments", CreateEnrollment);
        api.MapPost("/device-sessions/{id:guid}/revoke", RevokeSession);
        return app;
    }

    private static async Task<IResult> CreateDevice(DeviceRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var code = request.Code?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();
        var platform = request.Platform?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(platform))
            return Validation("device", "code, name and platform are required.");
        var now = DateTimeOffset.UtcNow;
        var device = new Device
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, Code = code, Name = name,
            Platform = platform, IsEnabled = true, CreatedAt = now
        };
        db.AddRange(device, Audit(tenant, "device.created", "Device", device.Id, new { device.Code, device.Name, device.Platform }, now));
        return await SaveCreated(db, device, $"/api/v1/devices/{device.Id}", "DEVICE_EXISTS", "Device already exists.");
    }

    private static async Task<IResult> CreateStation(StationRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var code = request.Code?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)
            || request.SiteId == Guid.Empty || request.ReadPointId == Guid.Empty || request.BusinessLocationId == Guid.Empty)
            return Validation("station", "code, name, siteId, readPointId and businessLocationId are required.");
        if (!await db.Sites.AnyAsync(x => x.Id == request.SiteId)) return Validation("siteId", "Site was not found.");
        var locationIds = new[] { request.ReadPointId, request.BusinessLocationId }.Distinct().ToArray();
        if (await db.Locations.CountAsync(x => locationIds.Contains(x.Id) && x.SiteId == request.SiteId) != locationIds.Length)
            return Validation("location", "Station locations must belong to the selected site.");
        var now = DateTimeOffset.UtcNow;
        var station = new Station
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, Code = code, Name = name,
            SiteId = request.SiteId, ReadPointId = request.ReadPointId,
            BusinessLocationId = request.BusinessLocationId, IsEnabled = true, CreatedAt = now
        };
        db.AddRange(station, Audit(tenant, "station.created", "Station", station.Id,
            new { station.Code, station.Name, station.SiteId, station.ReadPointId, station.BusinessLocationId }, now));
        return await SaveCreated(db, station, $"/api/v1/stations/{station.Id}", "STATION_EXISTS", "Station already exists.");
    }

    private static async Task<IResult> CreateEnrollment(DeviceEnrollmentRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = request.ExpiresAt ?? now.AddMinutes(15);
        if (expiresAt <= now || expiresAt > now.AddHours(24))
            return Validation("expiresAt", "Enrollment must expire within 24 hours.");
        if (!await db.Devices.AnyAsync(x => x.Id == request.DeviceId && x.IsEnabled)
            || !await db.Stations.AnyAsync(x => x.Id == request.StationId && x.IsEnabled))
            return Validation("enrollment", "Enabled device and station are required.");
        var code = CaptureSecrets.CreateToken();
        var enrollment = new DeviceEnrollment
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, DeviceId = request.DeviceId,
            StationId = request.StationId, EnrollmentCodeHash = CaptureSecrets.Hash(code),
            CreatedBySubject = tenant.UserSubject, CreatedAt = now, ExpiresAt = expiresAt
        };
        db.AddRange(enrollment, Audit(tenant, "device_enrollment.created", "DeviceEnrollment", enrollment.Id,
            new { enrollment.DeviceId, enrollment.StationId, enrollment.ExpiresAt }, now));
        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/device-enrollments/{enrollment.Id}", new
        {
            enrollment.Id,
            enrollment.DeviceId,
            enrollment.StationId,
            enrollment.ExpiresAt,
            enrollmentCode = code
        });
    }

    private static async Task<IResult> RevokeSession(Guid id, UnitAtlasDb db, ITenantContext tenant)
    {
        var session = await db.DeviceSessions.SingleOrDefaultAsync(x => x.Id == id);
        if (session is null) return Problem("DEVICE_SESSION_NOT_FOUND", "Device session not found.", 404);
        if (session.RevokedAt is not null) return Results.NoContent();
        var now = DateTimeOffset.UtcNow;
        session.RevokedAt = now;
        db.AuditEntries.Add(Audit(tenant, "device_session.revoked", "DeviceSession", id, new { session.DeviceId }, now));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static AuditEntry Audit(ITenantContext tenant, string action, string entityType, Guid entityId, object data, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ActorSubject = tenant.UserSubject,
        Action = action, EntityType = entityType, EntityId = entityId,
        DataJson = JsonSerializer.Serialize(data), CreatedAt = now
    };

    private static async Task<IResult> SaveCreated<TEntity>(UnitAtlasDb db, TEntity entity, string location, string code, string title)
    {
        try
        {
            await db.SaveChangesAsync();
            return Results.Created(location, entity);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(code, title, 409);
        }
    }

    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
    private static IResult Problem(string code, string title, int status) => Results.Problem(statusCode: status, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
