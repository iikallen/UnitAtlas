using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.RateLimiting;
using UnitAtlas.Application.Traceability;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Api;
using UnitAtlas.Api.Auth;
using UnitAtlas.Api.Observability;
using UnitAtlas.Contracts;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure;
using UnitAtlas.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddUnitAtlasSecurity(builder.Configuration, builder.Environment);
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("code", ErrorCode(context.ProblemDetails.Status));
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too Many Requests",
            extensions: new Dictionary<string, object?> { ["code"] = "RATE_LIMITED" })
            .ExecuteAsync(context.HttpContext);
    };
    options.AddFixedWindowLimiter("public-passport", limiter => ConfigureLimiter(limiter, 60));
    options.AddFixedWindowLimiter("unit-search", limiter => ConfigureLimiter(limiter, 60));
    options.AddFixedWindowLimiter("unit-lookup", limiter => ConfigureLimiter(limiter, 120));
    options.AddFixedWindowLimiter("event-ingest", limiter => ConfigureLimiter(limiter, 60));
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Cors:Origin"] ?? "http://localhost:3000")
        .AllowAnyHeader().AllowAnyMethod()));
var exportOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(Telemetry.Name))
    .WithTracing(tracing =>
    {
        tracing.AddSource(Telemetry.Name)
            .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health/live"))
            .AddHttpClientInstrumentation()
            .AddNpgsql();
        if (exportOtlp) tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(Telemetry.Name, "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "Npgsql")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (exportOtlp) metrics.AddOtlpExporter();
    });

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(async (context, next) =>
{
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseCors();
app.MapOpenApi();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
    await DatabaseInitializer.MigrateAndSeedAsync(app.Services, builder.Configuration);

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.Use(async (context, next) =>
{
    var tenant = context.RequestServices.GetRequiredService<ITenantContext>();
    using (app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["TraceId"] = Activity.Current?.TraceId.ToString(),
        ["RequestId"] = context.TraceIdentifier,
        ["TenantId"] = tenant.IsAvailable ? tenant.TenantId : null,
        ["UserSubject"] = tenant.IsAvailable ? tenant.UserSubject : null,
        ["AtlasId"] = context.Request.RouteValues["atlasId"]
    })) await next();
});
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new { name = "UnitAtlas API", version = "0.3.0" })).AllowAnonymous();
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (UnitAtlasDb db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.Problem("Database unavailable"));
app.MapGet("/health", async (UnitAtlasDb db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.Problem("Database unavailable"));

var publicApi = app.MapGroup("/api/public");
publicApi.MapGet("/passports/{publicId}", async (string publicId, UnitAtlasDb db, ITenantContext tenantContext) =>
{
    var config = await db.PublicPassportConfigs.AsNoTracking()
        .SingleOrDefaultAsync(x => x.PublicId == publicId && x.IsPublished);
    if (config is null) return Problem("PASSPORT_NOT_FOUND", "Passport not found.", StatusCodes.Status404NotFound);

    tenantContext.Initialize(config.TenantId, "public-passport", TenantRole.Viewer);
    try
    {
        var unit = await db.Units.AsNoTracking().Include(x => x.Product).SingleAsync(x => x.Id == config.UnitId);
        var state = await db.UnitStates.AsNoTracking().SingleAsync(x => x.UnitId == unit.Id);
        var timeline = await db.TraceEvents.AsNoTracking().Where(x => x.UnitId == unit.Id)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Sequence)
            .Select(x => new { code = x.EventType, x.OccurredAt })
            .ToListAsync();
        return Results.Ok(new
        {
            config.PublicId,
            authenticity = "verified",
            product = new { unit.Product.Name, unit.Product.Gtin },
            unit.Serial,
            unit.ManufacturedAt,
            state = new { state.Status, state.UpdatedAt },
            timeline
        });
    }
    finally
    {
        tenantContext.Clear();
    }
}).AllowAnonymous().RequireRateLimiting("public-passport");

var api = app.MapGroup("/api/v1");
api.RequireAuthorization();

api.MapGet("/me", (ITenantContext tenantContext) => Results.Ok(new
{
    tenantContext.UserSubject,
    tenantContext.TenantId,
    role = tenantContext.Role.ToString(),
    permissions = tenantContext.GrantedPermissions
})).RequireAuthorization(Permissions.UnitsRead);

api.MapGet("/dashboard", async (UnitAtlasDb db) =>
{
    var counts = await db.UnitStates.GroupBy(x => x.Status)
        .Select(x => new { status = x.Key, count = x.Count() }).ToListAsync();
    var units = await UnitQuery(db).Take(8).ToListAsync();
    return Results.Ok(new
    {
        totalUnits = await db.Units.CountAsync(),
        products = await db.Products.CountAsync(),
        events = await db.TraceEvents.CountAsync(),
        statuses = counts,
        recentUnits = units
    });
}).RequireAuthorization(Permissions.UnitsRead);

api.MapGet("/products", async (UnitAtlasDb db) => Results.Ok(await db.Products
    .OrderBy(x => x.Name)
    .Select(x => new { x.Id, x.Sku, x.Name, x.Gtin })
    .ToListAsync())).RequireAuthorization(Permissions.UnitsRead);

api.MapGet("/sites", async (UnitAtlasDb db) => Results.Ok(await db.Sites.AsNoTracking()
    .OrderBy(site => site.Name)
    .Select(site => new { site.Id, site.Code, site.Name })
    .ToListAsync())).RequireAuthorization(Permissions.UnitsRead);

api.MapGet("/locations", async (Guid? siteId, UnitAtlasDb db) => Results.Ok(await db.Locations.AsNoTracking()
    .Where(location => siteId == null || location.SiteId == siteId)
    .OrderBy(location => location.Name)
    .Select(location => new { location.Id, location.SiteId, location.ParentLocationId, location.Code, location.Name, location.Type })
    .ToListAsync())).RequireAuthorization(Permissions.UnitsRead);

api.MapPost("/products", async (ProductRequest request, UnitAtlasDb db, ITenantContext tenantContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Sku) || string.IsNullOrWhiteSpace(request.Name)
        || string.IsNullOrWhiteSpace(request.Gtin) || request.Gtin.Length is < 8 or > 14)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["SKU, name and an 8-14 digit GTIN are required."] });

    var product = new Product
    {
        Id = Guid.NewGuid(),
        TenantId = tenantContext.TenantId,
        Sku = request.Sku.Trim(),
        Name = request.Name.Trim(),
        Gtin = request.Gtin.Trim(),
        CreatedAt = DateTimeOffset.UtcNow
    };
    db.AddRange(product,
        new ProductIdentifier { Id = Guid.NewGuid(), TenantId = product.TenantId, ProductId = product.Id, Type = "SKU", Value = product.Sku },
        new ProductIdentifier { Id = Guid.NewGuid(), TenantId = product.TenantId, ProductId = product.Id, Type = "GTIN", Value = product.Gtin });
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/products/{product.Id}", new { product.Id, product.Sku, product.Name, product.Gtin });
}).RequireAuthorization(Permissions.ProductsManage);

api.MapGet("/units", async (string? query, string? cursor, int? limit, HttpResponse response, UnitAtlasDb db) =>
{
    var pageSize = Math.Clamp(limit ?? 50, 1, 100);
    var units = await UnitQuery(db, query, cursor, orderByAtlasId: true)
        .Take(pageSize + 1)
        .ToListAsync();
    if (units.Count > pageSize)
    {
        units.RemoveAt(pageSize);
        response.Headers["X-Next-Cursor"] = units[^1].AtlasId;
    }
    return Results.Ok(units);
})
    .RequireAuthorization(Permissions.UnitsRead)
    .RequireRateLimiting("unit-search");

api.MapPost("/units", async (UnitRequest request, UnitAtlasDb db, ITenantContext tenantContext) =>
{
    var product = await db.Products.FindAsync(request.ProductId);
    if (product is null) return Problem("PRODUCT_NOT_FOUND", "Product not found.", StatusCodes.Status404NotFound);
    if (string.IsNullOrWhiteSpace(request.Serial) || string.IsNullOrWhiteSpace(request.Lot))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["unit"] = ["Serial and lot are required."] });

    var now = DateTimeOffset.UtcNow;
    var lot = await db.Lots.SingleOrDefaultAsync(x => x.ProductId == product.Id && x.Code == request.Lot.Trim());
    if (lot is null)
    {
        lot = new Lot
        {
            Id = Guid.NewGuid(),
            TenantId = product.TenantId,
            ProductId = product.Id,
            Code = request.Lot.Trim(),
            ManufacturedAt = request.ManufacturedAt ?? now
        };
        db.Lots.Add(lot);
    }
    var unit = new TrackedUnit
    {
        Id = Guid.NewGuid(),
        TenantId = product.TenantId,
        ProductId = product.Id,
        AtlasId = $"UA-KZ-{now:yyyy}-{Random.Shared.NextInt64(0, 10_000_000_000):D10}",
        Serial = request.Serial.Trim(),
        Lot = request.Lot.Trim(),
        LotId = lot.Id,
        ManufacturedAt = request.ManufacturedAt ?? now,
        CreatedAt = now
    };
    var trace = NewEvent(unit, "MANUFACTURED", "Factory #1", $"unit-create:{unit.Id}", "system", unit.ManufacturedAt, 1);
    db.AddRange(unit, trace, NewState(unit, trace, "Manufactured"),
        new UnitIdentifier { Id = Guid.NewGuid(), TenantId = unit.TenantId, UnitId = unit.Id, Type = "ATLAS_ID", Value = unit.AtlasId },
        new UnitIdentifier { Id = Guid.NewGuid(), TenantId = unit.TenantId, UnitId = unit.Id, Type = "SERIAL", Value = unit.Serial },
        new PublicPassportConfig { UnitId = unit.Id, TenantId = unit.TenantId, PublicId = Guid.NewGuid().ToString("N"), IsPublished = false },
        new AuditEntry { Id = Guid.CreateVersion7(), TenantId = unit.TenantId, ActorSubject = tenantContext.UserSubject,
            Action = "unit.created", EntityType = "TrackedUnit", EntityId = unit.Id,
            DataJson = JsonSerializer.Serialize(new { unit.AtlasId, unit.ProductId, unit.Serial, unit.Lot }), CreatedAt = now },
        new OutboxMessage { Id = Guid.CreateVersion7(), TenantId = unit.TenantId, CorrelationId = unit.Id,
            Source = "unitatlas", Type = "unit.created", SubjectType = "TrackedUnit", SubjectId = unit.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new { unit.Id, unit.AtlasId, unit.ProductId, unit.Serial, unit.Lot, unit.ManufacturedAt }), CreatedAt = now },
        new OutboxMessage { Id = Guid.CreateVersion7(), TenantId = unit.TenantId, CorrelationId = unit.Id,
            Source = "unitatlas", Type = "trace_event.recorded", SubjectType = "TraceEvent", SubjectId = trace.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new { trace.Id, unit.AtlasId, trace.EventType, trace.Location, trace.OccurredAt, trace.SourceSystem }), CreatedAt = now });
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/units/{unit.AtlasId}", new { unit.AtlasId });
}).RequireAuthorization(Permissions.UnitsCreate);

api.MapGet("/units/{atlasId}", InternalPassport)
    .RequireAuthorization(Permissions.UnitsRead).RequireRateLimiting("unit-lookup");
api.MapGet("/passports/{atlasId}", InternalPassport)
    .RequireAuthorization(Permissions.UnitsRead).RequireRateLimiting("unit-lookup");

api.MapGet("/units/{atlasId}/events", async (string atlasId, UnitAtlasDb db) =>
{
    var unitId = await db.Units.Where(unit => unit.AtlasId == atlasId).Select(unit => (Guid?)unit.Id).SingleOrDefaultAsync();
    return unitId is null
        ? Problem("UNIT_NOT_FOUND", "Unit not found.", StatusCodes.Status404NotFound)
        : Results.Ok(await EventQuery(db, unitId.Value).ToListAsync());
}).RequireAuthorization(Permissions.UnitsRead).RequireRateLimiting("unit-lookup");

api.MapPost("/units/{atlasId}/events", async (string atlasId, EventRequest request, UnitAtlasDb db, ITenantContext tenantContext, ILogger<Program> logger) =>
{
    using var ingestActivity = Telemetry.Activities.StartActivity("unitatlas.event.ingest");
    ingestActivity?.SetTag("unitatlas.atlas_id", atlasId);
    if (!TraceEventProjection.TryGetStatus(request.EventType ?? "", out var status))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["eventType"] = [$"Allowed: {string.Join(", ", TraceEventProjection.EventTypes)}"] });
    if (string.IsNullOrWhiteSpace(request.Location) || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["event"] = ["Location and idempotencyKey are required."] });

    var locationIds = new[] { request.ReadPointId, request.BusinessLocationId }.OfType<Guid>().Distinct().ToArray();
    if (locationIds.Length > 0 && await db.Locations.CountAsync(location => locationIds.Contains(location.Id)) != locationIds.Length)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["location"] = ["readPointId and businessLocationId must belong to the active tenant."] });

    var unit = await db.Units.SingleOrDefaultAsync(x => x.AtlasId == atlasId);
    if (unit is null) return Problem("UNIT_NOT_FOUND", "Unit not found.", StatusCodes.Status404NotFound);
    var operation = $"unit:{unit.Id}:event";
    var requestHash = EventRequestHash.Compute(atlasId, request);
    var idempotencyKey = request.IdempotencyKey.Trim();
    var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Key == idempotencyKey);
    if (existing is not null) return IdempotencyResult(existing, operation, requestHash);

    await using var transaction = await db.Database.BeginTransactionAsync();
    try
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM units WHERE \"Id\" = {unit.Id} FOR UPDATE");
        existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Key == idempotencyKey);
        if (existing is not null)
        {
            await transaction.RollbackAsync();
            return IdempotencyResult(existing, operation, requestHash);
        }

        var sequence = (await db.TraceEvents.Where(x => x.UnitId == unit.Id).MaxAsync(x => (long?)x.Sequence) ?? 0) + 1;
        var trace = NewEvent(unit, request.EventType!.ToUpperInvariant(), request.Location!.Trim(), idempotencyKey, request.Actor?.Trim() ?? "operator", request.OccurredAt ?? DateTimeOffset.UtcNow, sequence);
        trace.ActorSubject = tenantContext.UserSubject;
        trace.ReadPointId = request.ReadPointId;
        trace.BusinessLocationId = request.BusinessLocationId;
        trace.BusinessStep = request.BusinessStep?.Trim();
        trace.Disposition = request.Disposition?.Trim();
        trace.SourceSystem = request.SourceSystem?.Trim() ?? "unitatlas";
        var state = await db.UnitStates.SingleAsync(x => x.UnitId == unit.Id);
        TraceEventProjection.Apply(state, trace, status);
        var now = DateTimeOffset.UtcNow;
        db.AddRange(trace,
            new IdempotencyRecord
            {
                Id = Guid.NewGuid(),
                TenantId = unit.TenantId,
                Key = idempotencyKey,
                Operation = operation,
                RequestHash = requestHash,
                ResourceId = trace.Id,
                ResponseStatus = StatusCodes.Status201Created,
                CreatedAt = now,
                ExpiresAt = now.AddHours(24)
            },
            new AuditEntry
            {
                Id = Guid.NewGuid(),
                TenantId = unit.TenantId,
                ActorSubject = tenantContext.UserSubject,
                Action = "trace_event.recorded",
                EntityType = "TraceEvent",
                EntityId = trace.Id,
                DataJson = JsonSerializer.Serialize(new { unit.AtlasId, trace.EventType, trace.OccurredAt }),
                CreatedAt = now
            },
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                TenantId = unit.TenantId,
                CorrelationId = trace.Id,
                Source = "unitatlas",
                Type = "trace_event.recorded",
                SubjectType = "TraceEvent",
                SubjectId = trace.Id.ToString(),
                PayloadJson = JsonSerializer.Serialize(new { trace.Id, unit.AtlasId }),
                CreatedAt = now
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
        return IdempotencyResult(winner, operation, requestHash);
    }
}).RequireAuthorization(Permissions.EventsRecord).RequireRateLimiting("event-ingest");

app.MapPackagingEndpoints();
app.MapIntegrationEndpoints();
app.MapOneCEndpoints();
app.MapEpcisEndpoints();
app.Run();

static IQueryable<UnitSummary> UnitQuery(UnitAtlasDb db, string? query = null, string? cursor = null, bool orderByAtlasId = false)
{
    var rows = from unit in db.Units.AsNoTracking()
               join product in db.Products.AsNoTracking() on unit.ProductId equals product.Id
               join state in db.UnitStates.AsNoTracking() on unit.Id equals state.UnitId
               where string.IsNullOrWhiteSpace(query) || unit.AtlasId.Contains(query) || unit.Serial.Contains(query) || product.Gtin.Contains(query)
               select new { Unit = unit, Product = product, State = state };
    if (cursor is not null) rows = rows.Where(row => row.Unit.AtlasId.CompareTo(cursor) > 0);
    var ordered = orderByAtlasId ? rows.OrderBy(row => row.Unit.AtlasId) : rows.OrderByDescending(row => row.State.UpdatedAt);
    return ordered.Select(row => new UnitSummary(row.Unit.AtlasId, row.Unit.Serial, row.Unit.Lot, row.Product.Name,
        row.Product.Sku, row.Product.Gtin, row.State.Status, row.State.Location, row.State.UpdatedAt));
}

static IQueryable<TraceEventResponse> EventQuery(UnitAtlasDb db, Guid unitId) => db.TraceEvents.AsNoTracking()
    .Where(trace => trace.UnitId == unitId)
    .OrderByDescending(trace => trace.OccurredAt)
    .ThenByDescending(trace => trace.Sequence)
    .Select(trace => new TraceEventResponse(trace.Id, trace.EventType, trace.Location, trace.Actor, trace.ActorSubject,
        trace.SourceSystem, trace.OccurredAt, trace.RecordedAt, trace.Sequence, trace.ReadPointId,
        trace.BusinessLocationId, trace.BusinessStep, trace.Disposition));

static async Task<IResult> InternalPassport(string atlasId, UnitAtlasDb db)
{
    var unit = await db.Units.AsNoTracking().Include(tracked => tracked.Product)
        .SingleOrDefaultAsync(tracked => tracked.AtlasId == atlasId);
    if (unit is null) return Problem("UNIT_NOT_FOUND", "Unit not found.", StatusCodes.Status404NotFound);
    var state = await db.UnitStates.AsNoTracking().SingleAsync(current => current.UnitId == unit.Id);
    return Results.Ok(new
    {
        unit.AtlasId,
        unit.Serial,
        unit.Lot,
        unit.ManufacturedAt,
        product = new { unit.Product.Name, unit.Product.Sku, unit.Product.Gtin },
        state = new { state.Status, state.Location, state.UpdatedAt },
        events = await EventQuery(db, unit.Id).ToListAsync()
    });
}

static TraceEvent NewEvent(TrackedUnit unit, string type, string location, string key, string actor, DateTimeOffset occurredAt, long sequence) => new()
{
    Id = Guid.CreateVersion7(),
    TenantId = unit.TenantId,
    UnitId = unit.Id,
    EventType = type,
    Location = location,
    Actor = actor,
    SourceSystem = "unitatlas",
    IdempotencyKey = key,
    OccurredAt = occurredAt,
    RecordedAt = DateTimeOffset.UtcNow,
    Sequence = sequence
};

static UnitState NewState(TrackedUnit unit, TraceEvent trace, string status) => new()
{
    UnitId = unit.Id,
    TenantId = unit.TenantId,
    Status = status,
    Location = trace.Location,
    LastEventId = trace.Id,
    CurrentOccurredAt = trace.OccurredAt,
    CurrentSequence = trace.Sequence,
    UpdatedAt = trace.RecordedAt
};

static IResult IdempotencyResult(IdempotencyRecord record, string operation, string requestHash) =>
    record.Operation == operation && record.RequestHash == requestHash
        ? Results.Json(new { id = record.ResourceId, duplicate = true }, statusCode: record.ResponseStatus)
        : Problem("IDEMPOTENCY_KEY_REUSED", "Idempotency key reused.", StatusCodes.Status409Conflict,
            "The idempotency key was already used with a different request.");

static IResult Problem(string code, string title, int status, string? detail = null) =>
    Results.Problem(statusCode: status, title: title, detail: detail,
        extensions: new Dictionary<string, object?> { ["code"] = code });

static string ErrorCode(int? status) => status switch
{
    StatusCodes.Status400BadRequest => "VALIDATION_ERROR",
    StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
    StatusCodes.Status403Forbidden => "FORBIDDEN",
    StatusCodes.Status404NotFound => "NOT_FOUND",
    StatusCodes.Status409Conflict => "CONFLICT",
    StatusCodes.Status429TooManyRequests => "RATE_LIMITED",
    _ => "INTERNAL_ERROR"
};

static void ConfigureLimiter(FixedWindowRateLimiterOptions options, int permitLimit)
{
    options.PermitLimit = permitLimit;
    options.Window = TimeSpan.FromMinutes(1);
    options.QueueLimit = 0;
    options.AutoReplenishment = true;
}

public partial class Program;
