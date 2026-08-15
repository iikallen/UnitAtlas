using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using UnitAtlas.Application.Traceability;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Api.Auth;
using UnitAtlas.Contracts;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure;
using UnitAtlas.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddUnitAtlasSecurity(builder.Configuration, builder.Environment);
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Cors:Origin"] ?? "http://localhost:3000")
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.MapOpenApi();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
    await DatabaseInitializer.MigrateAndSeedAsync(app.Services, builder.Configuration);

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { name = "UnitAtlas API", version = "0.1.0" })).AllowAnonymous();
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
    if (config is null) return Results.NotFound(new { code = "PASSPORT_NOT_FOUND" });

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
}).AllowAnonymous();

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

api.MapGet("/units", async (string? query, UnitAtlasDb db) =>
    Results.Ok(await UnitQuery(db, query).Take(100).ToListAsync()))
    .RequireAuthorization(Permissions.UnitsRead);

api.MapPost("/units", async (UnitRequest request, UnitAtlasDb db) =>
{
    var product = await db.Products.FindAsync(request.ProductId);
    if (product is null) return Results.NotFound(new { message = "Product not found." });
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
        new PublicPassportConfig { UnitId = unit.Id, TenantId = unit.TenantId, PublicId = Guid.NewGuid().ToString("N"), IsPublished = false });
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/units/{unit.AtlasId}", new { unit.AtlasId });
}).RequireAuthorization(Permissions.UnitsCreate);

api.MapGet("/units/{atlasId}", async (string atlasId, UnitAtlasDb db) =>
{
    var unit = await db.Units.AsNoTracking().Include(x => x.Product).SingleOrDefaultAsync(x => x.AtlasId == atlasId);
    if (unit is null) return Results.NotFound(new { message = "Unit not found." });
    var state = await db.UnitStates.AsNoTracking().SingleAsync(x => x.UnitId == unit.Id);
    var events = await db.TraceEvents.AsNoTracking().Where(x => x.UnitId == unit.Id)
        .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Sequence)
        .Select(x => new { x.Id, x.EventType, x.Location, x.Actor, x.OccurredAt, x.RecordedAt, x.Sequence })
        .ToListAsync();
    return Results.Ok(new
    {
        unit.AtlasId,
        unit.Serial,
        unit.Lot,
        unit.ManufacturedAt,
        product = new { unit.Product.Name, unit.Product.Sku, unit.Product.Gtin },
        state = new { state.Status, state.Location, state.UpdatedAt },
        events
    });
}).RequireAuthorization(Permissions.UnitsRead);

api.MapPost("/units/{atlasId}/events", async (string atlasId, EventRequest request, UnitAtlasDb db, ITenantContext tenantContext) =>
{
    if (!TraceEventProjection.TryGetStatus(request.EventType ?? "", out var status))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["eventType"] = [$"Allowed: {string.Join(", ", TraceEventProjection.EventTypes)}"] });
    if (string.IsNullOrWhiteSpace(request.Location) || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["event"] = ["Location and idempotencyKey are required."] });

    var unit = await db.Units.SingleOrDefaultAsync(x => x.AtlasId == atlasId);
    if (unit is null) return Results.NotFound(new { message = "Unit not found." });
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
                Type = "trace_event.recorded",
                PayloadJson = JsonSerializer.Serialize(new { trace.Id, unit.AtlasId }),
                CreatedAt = now
            });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
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
}).RequireAuthorization(Permissions.EventsRecord);

app.Run();

static IQueryable<UnitSummary> UnitQuery(UnitAtlasDb db, string? query = null) =>
    from unit in db.Units.AsNoTracking()
    join product in db.Products.AsNoTracking() on unit.ProductId equals product.Id
    join state in db.UnitStates.AsNoTracking() on unit.Id equals state.UnitId
    where string.IsNullOrWhiteSpace(query) || unit.AtlasId.Contains(query) || unit.Serial.Contains(query) || product.Gtin.Contains(query)
    orderby state.UpdatedAt descending
    select new UnitSummary(unit.AtlasId, unit.Serial, unit.Lot, product.Name, product.Sku,
        product.Gtin, state.Status, state.Location, state.UpdatedAt);

static TraceEvent NewEvent(TrackedUnit unit, string type, string location, string key, string actor, DateTimeOffset occurredAt, long sequence) => new()
{
    Id = Guid.NewGuid(),
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
        : Results.Conflict(new { code = "IDEMPOTENCY_KEY_REUSED", message = "The idempotency key was already used with a different request." });

public partial class Program;
