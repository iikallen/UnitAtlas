using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api;

public sealed class CaptureDeviceContext
{
    public bool IsAvailable { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid StationId { get; private set; }
    public Guid SiteId { get; private set; }
    public Guid ReadPointId { get; private set; }
    public Guid BusinessLocationId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string DeviceCode { get; private set; } = "";
    public string DeviceName { get; private set; } = "";
    public string StationCode { get; private set; } = "";
    public string StationName { get; private set; } = "";

    internal void Initialize(
        Guid sessionId,
        Guid deviceId,
        Guid stationId,
        Guid siteId,
        Guid readPointId,
        Guid businessLocationId,
        DateTimeOffset expiresAt,
        string deviceCode,
        string deviceName,
        string stationCode,
        string stationName)
    {
        IsAvailable = true;
        SessionId = sessionId;
        DeviceId = deviceId;
        StationId = stationId;
        SiteId = siteId;
        ReadPointId = readPointId;
        BusinessLocationId = businessLocationId;
        ExpiresAt = expiresAt;
        DeviceCode = deviceCode;
        DeviceName = deviceName;
        StationCode = stationCode;
        StationName = stationName;
    }
}

internal static class CaptureSecrets
{
    internal static string CreateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class CaptureSessionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var token = http.Request.Headers["X-UnitAtlas-Device-Session"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized("DEVICE_SESSION_REQUIRED", "Device session is required.");

        var db = http.RequestServices.GetRequiredService<UnitAtlasDb>();
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        var now = DateTimeOffset.UtcNow;
        var hash = CaptureSecrets.Hash(token);
        var row = await (
            from session in db.DeviceSessions.AsNoTracking()
            join device in db.Devices.AsNoTracking() on session.DeviceId equals device.Id
            join station in db.Stations.AsNoTracking() on session.StationId equals station.Id
            where session.TokenHash == hash
                && session.UserSubject == tenant.UserSubject
                && session.RevokedAt == null
                && session.ExpiresAt > now
                && device.IsEnabled
                && station.IsEnabled
            select new
            {
                Session = session,
                Device = device,
                Station = station
            }).SingleOrDefaultAsync(http.RequestAborted);
        if (row is null) return Unauthorized("DEVICE_SESSION_INVALID", "Device session is invalid or expired.");

        http.RequestServices.GetRequiredService<CaptureDeviceContext>().Initialize(
            row.Session.Id,
            row.Device.Id,
            row.Station.Id,
            row.Station.SiteId,
            row.Station.ReadPointId,
            row.Station.BusinessLocationId,
            row.Session.ExpiresAt,
            row.Device.Code,
            row.Device.Name,
            row.Station.Code,
            row.Station.Name);
        if (row.Session.LastSeenAt < now.AddMinutes(-5))
            await db.DeviceSessions.Where(x => x.Id == row.Session.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(session => session.LastSeenAt, now), http.RequestAborted);
        return await next(context);
    }

    private static IResult Unauthorized(string code, string title) => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
