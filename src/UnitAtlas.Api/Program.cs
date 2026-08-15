using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnitAtlas.Application.Traceability;
using UnitAtlas.Contracts;
using UnitAtlas.Domain;
using UnitAtlas.Infrastructure;
using UnitAtlas.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Cors:Origin"] ?? "http://localhost:3000")
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.MapOpenApi();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
    await DatabaseInitializer.MigrateAndSeedAsync(app.Services, builder.Configuration);

app.MapGet("/", () => Results.Ok(new { name = "UnitAtlas API", version = "0.1.0" }));
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (UnitAtlasDb db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.Problem("Database unavailable"));
app.MapGet("/health", async (UnitAtlasDb db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.Problem("Database unavailable"));

var api = app.MapGroup("/api");

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
});

api.MapGet("/products", async (UnitAtlasDb db) => Results.Ok(await db.Products
    .OrderBy(x => x.Name)
    .Select(x => new { x.Id, x.Sku, x.Name, x.Gtin })
    .ToListAsync()));

api.MapPost("/products", async (ProductRequest request, UnitAtlasDb db) =>
{
    if (string.IsNullOrWhiteSpace(request.Sku) || string.IsNullOrWhiteSpace(request.Name)
        || string.IsNullOrWhiteSpace(request.Gtin) || request.Gtin.Length is < 8 or > 14)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["SKU, name and an 8-14 digit GTIN are required."] });

    var tenantId = await db.Tenants.Select(x => x.Id).SingleAsync();
    var product = new Product
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Sku = request.Sku.Trim(),
        Name = request.Name.Trim(),
        Gtin = request.Gtin.Trim(),
        CreatedAt = DateTimeOffset.UtcNow
    };
    db.Products.Add(product);
    await db.SaveChangesAsync();
    return Results.Created($"/api/products/{product.Id}", new { product.Id, product.Sku, product.Name, product.Gtin });
});

api.MapGet("/units", async (string? query, UnitAtlasDb db) =>
    Results.Ok(await UnitQuery(db, query).Take(100).ToListAsync()));

api.MapPost("/units", async (UnitRequest request, UnitAtlasDb db) =>
{
    var product = await db.Products.FindAsync(request.ProductId);
    if (product is null) return Results.NotFound(new { message = "Product not found." });
    if (string.IsNullOrWhiteSpace(request.Serial) || string.IsNullOrWhiteSpace(request.Lot))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["unit"] = ["Serial and lot are required."] });

    var now = DateTimeOffset.UtcNow;
    var unit = new TrackedUnit
    {
        Id = Guid.NewGuid(),
        TenantId = product.TenantId,
        ProductId = product.Id,
        AtlasId = $"UA-KZ-{now:yyyy}-{Random.Shared.NextInt64(0, 10_000_000_000):D10}",
        Serial = request.Serial.Trim(),
        Lot = request.Lot.Trim(),
        ManufacturedAt = request.ManufacturedAt ?? now,
        CreatedAt = now
    };
    var trace = NewEvent(unit, "MANUFACTURED", "Factory #1", $"unit-create:{unit.Id}", "system", unit.ManufacturedAt, 1);
    db.Units.Add(unit);
    db.TraceEvents.Add(trace);
    db.UnitStates.Add(NewState(unit, trace, "Manufactured"));
    await db.SaveChangesAsync();
    return Results.Created($"/api/units/{unit.AtlasId}", new { unit.AtlasId });
});

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
});

api.MapPost("/units/{atlasId}/events", async (string atlasId, EventRequest request, UnitAtlasDb db) =>
{
    if (!TraceEventProjection.TryGetStatus(request.EventType ?? "", out var status))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["eventType"] = [$"Allowed: {string.Join(", ", TraceEventProjection.EventTypes)}"] });
    if (string.IsNullOrWhiteSpace(request.Location) || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["event"] = ["Location and idempotencyKey are required."] });

    var unit = await db.Units.SingleOrDefaultAsync(x => x.AtlasId == atlasId);
    if (unit is null) return Results.NotFound(new { message = "Unit not found." });
    var existing = await db.TraceEvents.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == unit.TenantId && x.IdempotencyKey == request.IdempotencyKey);
    if (existing is not null) return Results.Ok(new { existing.Id, duplicate = true });

    var sequence = (await db.TraceEvents.Where(x => x.UnitId == unit.Id).MaxAsync(x => (long?)x.Sequence) ?? 0) + 1;
    var trace = NewEvent(unit, request.EventType!.ToUpperInvariant(), request.Location!.Trim(), request.IdempotencyKey!.Trim(), request.Actor?.Trim() ?? "operator", request.OccurredAt ?? DateTimeOffset.UtcNow, sequence);
    var state = await db.UnitStates.SingleAsync(x => x.UnitId == unit.Id);
    TraceEventProjection.Apply(state, trace, status);
    db.TraceEvents.Add(trace);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
        db.ChangeTracker.Clear();
        var winner = await db.TraceEvents.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == unit.TenantId && x.IdempotencyKey == request.IdempotencyKey);
        if (winner is null) throw;
        return Results.Ok(new { winner.Id, duplicate = true });
    }
    return Results.Created($"/api/units/{atlasId}", new { trace.Id, duplicate = false });
});

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

public partial class Program;
