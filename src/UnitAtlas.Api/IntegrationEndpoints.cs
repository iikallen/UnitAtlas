using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure.Persistence;

namespace UnitAtlas.Api;

public static class IntegrationEndpoints
{
    public static WebApplication MapIntegrationEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/integration-endpoints").RequireAuthorization();
        api.MapGet("/", ListAsync).RequireAuthorization(Permissions.IntegrationsRead);
        api.MapPost("/", CreateAsync).RequireAuthorization(Permissions.IntegrationsManage);
        api.MapGet("/{endpointId:guid}/deliveries", DeliveriesAsync).RequireAuthorization(Permissions.IntegrationsRead);
        api.MapPost("/{endpointId:guid}/inbox", ReceiveAsync).RequireAuthorization(Permissions.IntegrationsManage);
        app.MapPost("/api/v1/integration-inbox/{system}", ReceiveBySystemAsync)
            .RequireAuthorization(Permissions.IntegrationsManage);
        return app;
    }

    private static async Task<IResult> ListAsync(UnitAtlasDb db) => Results.Ok(await db.IntegrationEndpoints
        .AsNoTracking().OrderBy(x => x.System)
        .Select(x => new { x.Id, x.System, x.Adapter, x.BaseAddress, x.SecretRef, x.Enabled, x.CreatedAt, x.UpdatedAt })
        .ToListAsync());

    private static async Task<IResult> DeliveriesAsync(Guid endpointId, UnitAtlasDb db)
    {
        if (!await db.IntegrationEndpoints.AnyAsync(x => x.Id == endpointId))
            return Problem("INTEGRATION_ENDPOINT_NOT_FOUND", "Integration endpoint not found.", 404);
        return Results.Ok(await db.IntegrationDeliveries.AsNoTracking()
            .Where(x => x.IntegrationEndpointId == endpointId)
            .OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new { x.Id, x.OutboxMessageId, status = x.Status.ToString(), x.AttemptCount,
                x.NextAttemptAt, x.LastAttemptAt, x.DeliveredAt, x.LastErrorCode, x.CreatedAt })
            .ToListAsync());
    }

    private static async Task<IResult> CreateAsync(
        CreateIntegrationEndpointRequest request,
        UnitAtlasDb db,
        ITenantContext tenant,
        IWebHostEnvironment environment)
    {
        var system = request.System.Trim().ToUpperInvariant();
        var adapter = request.Adapter.Trim().ToUpperInvariant();
        if (system.Length is < 1 or > 80 || adapter != "WEBHOOK")
            return Problem("INVALID_INTEGRATION_ENDPOINT", "System is required and adapter must be WEBHOOK.", 400);
        if (!Uri.TryCreate(request.BaseAddress, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && address.Scheme == Uri.UriSchemeHttp)))
            return Problem("INVALID_INTEGRATION_ENDPOINT", "BaseAddress must be an absolute HTTPS URL (HTTP is allowed only in development).", 400);
        if (request.SecretRef is { Length: > 120 } || ContainsSecret(request.Settings))
            return Problem("SECRET_IN_SETTINGS", "Settings cannot contain credentials; provide only SecretRef.", 400);

        var now = DateTimeOffset.UtcNow;
        var endpoint = new IntegrationEndpoint
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, System = system, Adapter = adapter,
            BaseAddress = address.ToString(), SecretRef = string.IsNullOrWhiteSpace(request.SecretRef) ? null : request.SecretRef.Trim(),
            SettingsJson = request.Settings.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "{}" : request.Settings.GetRawText(),
            Enabled = request.Enabled, CreatedAt = now, UpdatedAt = now
        };
        db.AddRange(endpoint, new AuditEntry
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ActorSubject = tenant.UserSubject,
            Action = "integration_endpoint.created", EntityType = "IntegrationEndpoint", EntityId = endpoint.Id,
            DataJson = JsonSerializer.Serialize(new { endpoint.System, endpoint.Adapter, endpoint.BaseAddress, endpoint.SecretRef, endpoint.Enabled }),
            CreatedAt = now
        });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem("INTEGRATION_SYSTEM_EXISTS", "An endpoint for this system already exists.", 409);
        }
        return Results.Created($"/api/v1/integration-endpoints/{endpoint.Id}", new { endpoint.Id });
    }

    private static async Task<IResult> ReceiveAsync(Guid endpointId, HttpRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var endpoint = await db.IntegrationEndpoints.AsNoTracking().SingleOrDefaultAsync(x => x.Id == endpointId && x.Enabled);
        return endpoint is null
            ? Problem("INTEGRATION_ENDPOINT_NOT_FOUND", "Enabled integration endpoint not found.", 404)
            : await ReceiveCoreAsync(endpoint, request, db, tenant);
    }

    private static async Task<IResult> ReceiveBySystemAsync(string system, HttpRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var normalized = system.Trim().ToUpperInvariant();
        var endpoint = await db.IntegrationEndpoints.AsNoTracking().SingleOrDefaultAsync(x => x.System == normalized && x.Enabled);
        return endpoint is null
            ? Problem("INTEGRATION_ENDPOINT_NOT_FOUND", "Enabled integration endpoint not found.", 404)
            : await ReceiveCoreAsync(endpoint, request, db, tenant);
    }

    private static async Task<IResult> ReceiveCoreAsync(IntegrationEndpoint endpoint, HttpRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var externalMessageId = request.Headers["X-External-Message-Id"].FirstOrDefault()
            ?? request.Headers["Idempotency-Key"].FirstOrDefault()
            ?? "";
        externalMessageId = externalMessageId.Trim();
        if (externalMessageId.Length is < 1 or > 200)
            return Problem("EXTERNAL_MESSAGE_ID_REQUIRED", "X-External-Message-Id is required.", 400);

        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted); }
        catch (JsonException) { return Problem("INVALID_JSON", "Request body must be valid JSON.", 400); }
        using (document)
        {
            var payload = document.RootElement.GetRawText();
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            var existing = await db.InboxMessages.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SourceSystem == endpoint.System && x.ExternalMessageId == externalMessageId);
            if (existing is not null) return Existing(existing, hash);

            var inbox = new InboxMessage
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, IntegrationEndpointId = endpoint.Id,
                SourceSystem = endpoint.System, ExternalMessageId = externalMessageId, PayloadHash = hash,
                PayloadJson = payload, ResultJson = "{}", ReceivedAt = DateTimeOffset.UtcNow
            };
            inbox.ResultJson = JsonSerializer.Serialize(new { inboxMessageId = inbox.Id, status = "accepted" });
            db.InboxMessages.Add(inbox);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.Entry(inbox).State = EntityState.Detached;
                existing = await db.InboxMessages.AsNoTracking()
                    .SingleAsync(x => x.SourceSystem == endpoint.System && x.ExternalMessageId == externalMessageId);
                return Existing(existing, hash);
            }
            return Results.Json(JsonDocument.Parse(inbox.ResultJson).RootElement, statusCode: 202);
        }
    }

    private static IResult Existing(InboxMessage existing, string hash) => existing.PayloadHash == hash
        ? Results.Json(JsonDocument.Parse(existing.ResultJson).RootElement, statusCode: 200)
        : Problem("INBOX_IDEMPOTENCY_CONFLICT", "ExternalMessageId was already used with a different payload.", 409);

    private static bool ContainsSecret(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                var name = property.Name.ToLowerInvariant();
                if (name.Contains("secret") || name.Contains("password") || name.Contains("token") || name is "apikey" or "api_key") return true;
                if (ContainsSecret(property.Value)) return true;
            }
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) if (ContainsSecret(item)) return true;
        return false;
    }

    private static IResult Problem(string code, string detail, int status) => Results.Problem(
        statusCode: status, title: code, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record CreateIntegrationEndpointRequest(
    string System,
    string Adapter,
    string BaseAddress,
    string? SecretRef,
    JsonElement Settings,
    bool Enabled = true);
