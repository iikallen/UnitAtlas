using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Api.Observability;
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
        api.MapPost("/{endpointId:guid}/enabled", SetEnabledAsync).RequireAuthorization(Permissions.IntegrationsManage);
        api.MapPost("/{endpointId:guid}/configuration", ConfigureAsync).RequireAuthorization(Permissions.IntegrationsManage);
        api.MapPost("/{endpointId:guid}/deliveries/{deliveryId:guid}/retry", RetryAsync).RequireAuthorization(Permissions.IntegrationsManage);
        api.MapPost("/{endpointId:guid}/inbox", ReceiveAsync).RequireAuthorization(Permissions.IntegrationsManage);
        var settings = app.MapGroup("/api/v1/integration-settings").RequireAuthorization();
        settings.MapGet("/regulatory-gateway", GetRegulatoryGatewayAsync).RequireAuthorization(Permissions.IntegrationsRead);
        settings.MapPost("/regulatory-gateway", SetRegulatoryGatewayAsync).RequireAuthorization(Permissions.IntegrationsManage);
        app.MapPost("/api/v1/integration-inbox/{system}", ReceiveBySystemAsync)
            .RequireAuthorization(Permissions.IntegrationsManage);
        return app;
    }

    private static async Task<IResult> ListAsync(UnitAtlasDb db)
    {
        var endpoints = await db.IntegrationEndpoints.AsNoTracking().OrderBy(x => x.System).ToListAsync();
        var stats = await db.IntegrationDeliveries.AsNoTracking().GroupBy(x => x.IntegrationEndpointId)
            .Select(group => new EndpointStats(
                group.Key,
                group.Max(x => x.DeliveredAt),
                group.Count(x => x.Status == IntegrationDeliveryStatus.Pending || x.Status == IntegrationDeliveryStatus.Retry || x.Status == IntegrationDeliveryStatus.Delivering),
                group.Sum(x => x.AttemptCount > 1 ? x.AttemptCount - 1 : 0),
                group.Count(x => x.Status == IntegrationDeliveryStatus.DeadLetter),
                group.Where(x => x.Status == IntegrationDeliveryStatus.Pending || x.Status == IntegrationDeliveryStatus.Retry || x.Status == IntegrationDeliveryStatus.Delivering)
                    .Min(x => (DateTimeOffset?)x.CreatedAt)))
            .ToDictionaryAsync(x => x.EndpointId);
        var now = DateTimeOffset.UtcNow;
        return Results.Ok(endpoints.Select(endpoint =>
        {
            stats.TryGetValue(endpoint.Id, out var value);
            return new
            {
                endpoint.Id, endpoint.System, endpoint.Adapter, endpoint.BaseAddress,
                hasSecretRef = endpoint.SecretRef is not null, endpoint.Enabled, endpoint.CreatedAt, endpoint.UpdatedAt,
                lastSuccessfulDelivery = value?.LastSuccessfulDelivery,
                backlog = value?.Backlog ?? 0,
                retryCount = value?.RetryCount ?? 0,
                deadLetters = value?.DeadLetters ?? 0,
                deliveryLagSeconds = value?.OldestBacklogAt is { } oldest ? Math.Max(0, (now - oldest).TotalSeconds) : 0
            };
        }));
    }

    private static async Task<IResult> DeliveriesAsync(Guid endpointId, UnitAtlasDb db)
    {
        if (!await db.IntegrationEndpoints.AnyAsync(x => x.Id == endpointId))
            return Problem("INTEGRATION_ENDPOINT_NOT_FOUND", "Integration endpoint not found.", 404);
        return Results.Ok(await (from delivery in db.IntegrationDeliveries.AsNoTracking()
            join message in db.OutboxMessages.AsNoTracking() on delivery.OutboxMessageId equals message.Id
            where delivery.IntegrationEndpointId == endpointId
            orderby delivery.CreatedAt descending
            select new { delivery.Id, delivery.OutboxMessageId, message.Type, status = delivery.Status.ToString(),
                delivery.AttemptCount, delivery.NextAttemptAt, delivery.LastAttemptAt, delivery.DeliveredAt,
                delivery.LastErrorCode, delivery.CreatedAt }).Take(100).ToListAsync());
    }

    private static async Task<IResult> CreateAsync(
        CreateIntegrationEndpointRequest request,
        UnitAtlasDb db,
        ITenantContext tenant,
        IWebHostEnvironment environment)
    {
        var system = request.System.Trim().ToUpperInvariant();
        var adapter = request.Adapter.Trim().ToUpperInvariant();
        if (system.Length is < 1 or > 80 || adapter is not ("WEBHOOK" or "ONE_C"))
            return Problem("INVALID_INTEGRATION_ENDPOINT", "System is required and adapter must be WEBHOOK or ONE_C.", 400);
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
            DataJson = JsonSerializer.Serialize(new { endpoint.System, endpoint.Adapter, endpoint.BaseAddress, hasSecretRef = endpoint.SecretRef is not null, endpoint.Enabled }),
            CreatedAt = now
        });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem("INTEGRATION_SYSTEM_EXISTS", "An endpoint for this system already exists.", 409);
        }
        return Results.Created($"/api/v1/integration-endpoints/{endpoint.Id}", new { endpoint.Id });
    }

    private static async Task<IResult> SetEnabledAsync(
        Guid endpointId, SetEndpointEnabledRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var endpoint = await db.IntegrationEndpoints.SingleOrDefaultAsync(x => x.Id == endpointId);
        if (endpoint is null) return Problem("INTEGRATION_ENDPOINT_NOT_FOUND", "Integration endpoint not found.", 404);
        if (endpoint.Enabled == request.Enabled) return Results.Ok(new { endpoint.Id, endpoint.Enabled });
        endpoint.Enabled = request.Enabled;
        endpoint.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEntries.Add(Audit(tenant, request.Enabled ? "integration_endpoint.enabled" : "integration_endpoint.disabled",
            "IntegrationEndpoint", endpoint.Id, new { endpoint.System, endpoint.Enabled }));
        await db.SaveChangesAsync();
        return Results.Ok(new { endpoint.Id, endpoint.Enabled });
    }

    private static async Task<IResult> ConfigureAsync(
        Guid endpointId, ConfigureIntegrationEndpointRequest request, UnitAtlasDb db,
        ITenantContext tenant, IWebHostEnvironment environment)
    {
        var endpoint = await db.IntegrationEndpoints.SingleOrDefaultAsync(x => x.Id == endpointId);
        if (endpoint is null) return Problem("INTEGRATION_ENDPOINT_NOT_FOUND", "Integration endpoint not found.", 404);
        if (!Uri.TryCreate(request.BaseAddress, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && address.Scheme == Uri.UriSchemeHttp)))
            return Problem("INVALID_INTEGRATION_ENDPOINT", "BaseAddress must be an absolute HTTPS URL (HTTP is allowed only in development).", 400);
        if (request.SecretRef is { Length: > 120 } || ContainsSecret(request.Settings))
            return Problem("SECRET_IN_SETTINGS", "Settings cannot contain credentials; provide only SecretRef.", 400);
        endpoint.BaseAddress = address.ToString();
        endpoint.SecretRef = string.IsNullOrWhiteSpace(request.SecretRef) ? null : request.SecretRef.Trim();
        endpoint.SettingsJson = request.Settings.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "{}" : request.Settings.GetRawText();
        endpoint.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEntries.Add(Audit(tenant, "integration_endpoint.configured", "IntegrationEndpoint", endpoint.Id,
            new { endpoint.System, endpoint.Adapter, endpoint.BaseAddress, hasSecretRef = endpoint.SecretRef is not null }));
        await db.SaveChangesAsync();
        return Results.Ok(new { endpoint.Id });
    }

    private static async Task<IResult> RetryAsync(
        Guid endpointId, Guid deliveryId, UnitAtlasDb db, ITenantContext tenant)
    {
        var delivery = await db.IntegrationDeliveries.SingleOrDefaultAsync(
            x => x.Id == deliveryId && x.IntegrationEndpointId == endpointId);
        if (delivery is null) return Problem("INTEGRATION_DELIVERY_NOT_FOUND", "Integration delivery not found.", 404);
        if (delivery.Status != IntegrationDeliveryStatus.DeadLetter)
            return Problem("INTEGRATION_DELIVERY_NOT_DEAD_LETTER", "Only dead-letter deliveries can be retried manually.", 409);
        var priorAttempts = delivery.AttemptCount;
        var priorError = delivery.LastErrorCode;
        delivery.Status = IntegrationDeliveryStatus.Pending;
        delivery.AttemptCount = 0;
        delivery.NextAttemptAt = DateTimeOffset.UtcNow;
        delivery.LastAttemptAt = null;
        delivery.DeliveredAt = null;
        delivery.LeaseUntil = null;
        delivery.LeaseToken = null;
        delivery.LastErrorCode = null;
        db.AuditEntries.Add(Audit(tenant, "integration_delivery.retried", "IntegrationDelivery", delivery.Id,
            new { delivery.IntegrationEndpointId, delivery.OutboxMessageId, priorAttempts, priorError }));
        await db.SaveChangesAsync();
        return Results.Accepted($"/api/v1/integration-endpoints/{endpointId}/deliveries", new { delivery.Id, status = "Pending" });
    }

    private static async Task<IResult> GetRegulatoryGatewayAsync(UnitAtlasDb db, ITenantContext tenant)
    {
        var mode = await db.Tenants.Where(x => x.Id == tenant.TenantId).Select(x => x.RegulatoryGatewayMode).SingleAsync();
        return Results.Ok(new { mode });
    }

    private static async Task<IResult> SetRegulatoryGatewayAsync(
        SetRegulatoryGatewayRequest request, UnitAtlasDb db, ITenantContext tenant)
    {
        var mode = request.Mode?.Trim().ToUpperInvariant() ?? "";
        if (mode is not ("NONE" or "ONE_C" or "DIRECT_IS_MPT"))
            return Problem("INVALID_REGULATORY_GATEWAY_MODE", "Allowed: NONE, ONE_C, DIRECT_IS_MPT.", 400);
        var current = await db.Tenants.SingleAsync(x => x.Id == tenant.TenantId);
        if (current.RegulatoryGatewayMode == mode) return Results.Ok(new { mode });
        var previous = current.RegulatoryGatewayMode;
        current.RegulatoryGatewayMode = mode;
        db.AuditEntries.Add(Audit(tenant, "regulatory_gateway.changed", "Tenant", tenant.TenantId,
            new { previous, mode }));
        await db.SaveChangesAsync();
        return Results.Ok(new { mode });
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

    private static IResult Existing(InboxMessage existing, string hash)
    {
        Telemetry.InboxDuplicates.Add(1, new KeyValuePair<string, object?>("integration.system", existing.SourceSystem));
        return existing.PayloadHash == hash
            ? Results.Json(JsonSerializer.Deserialize<JsonElement>(existing.ResultJson), statusCode: 200)
            : Problem("INBOX_IDEMPOTENCY_CONFLICT", "ExternalMessageId was already used with a different payload.", 409);
    }

    private static AuditEntry Audit(ITenantContext tenant, string action, string entityType, Guid entityId, object data) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, ActorSubject = tenant.UserSubject,
        Action = action, EntityType = entityType, EntityId = entityId,
        DataJson = JsonSerializer.Serialize(data), CreatedAt = DateTimeOffset.UtcNow
    };

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

public sealed record SetEndpointEnabledRequest(bool Enabled);
public sealed record ConfigureIntegrationEndpointRequest(string BaseAddress, string? SecretRef, JsonElement Settings);
public sealed record SetRegulatoryGatewayRequest(string Mode);
internal sealed record EndpointStats(Guid EndpointId, DateTimeOffset? LastSuccessfulDelivery, int Backlog,
    int RetryCount, int DeadLetters, DateTimeOffset? OldestBacklogAt);
