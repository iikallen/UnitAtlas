using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnitAtlas.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<UnitAtlasDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Cors:Origin"] ?? "http://localhost:3000")
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.MapOpenApi();

var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["MANUFACTURED"] = "Manufactured",
    ["QUALITY_PASSED"] = "QC passed",
    ["PACKED"] = "Packed",
    ["MOVED_TO_WAREHOUSE"] = "In warehouse",
    ["SHIPPED"] = "Shipped",
    ["RECEIVED"] = "Received"
};

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UnitAtlasDb>();
    await db.Database.EnsureCreatedAsync();
    await SeedAsync(db);
}

app.MapGet("/", () => Results.Ok(new { name = "UnitAtlas API", version = "0.1.0" }));
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
    if (string.IsNullOrWhiteSpace(request.Sku) || string.IsNullOrWhiteSpace(request.Name) || request.Gtin.Length is < 8 or > 14)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["SKU, name and an 8-14 digit GTIN are required."] });

    var tenantId = await db.Tenants.Select(x => x.Id).SingleAsync();
    var product = new Product
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Sku = request.Sku.Trim(), Name = request.Name.Trim(),
        Gtin = request.Gtin.Trim(), CreatedAt = DateTimeOffset.UtcNow
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
        Id = Guid.NewGuid(), TenantId = product.TenantId, ProductId = product.Id,
        AtlasId = $"UA-KZ-{now:yyyy}-{Random.Shared.NextInt64(0, 10_000_000_000):D10}",
        Serial = request.Serial.Trim(), Lot = request.Lot.Trim(), ManufacturedAt = request.ManufacturedAt ?? now, CreatedAt = now
    };
    var trace = NewEvent(unit, "MANUFACTURED", "Factory #1", $"unit-create:{unit.Id}", "system", unit.ManufacturedAt);
    db.Units.Add(unit);
    db.TraceEvents.Add(trace);
    db.UnitStates.Add(NewState(unit, trace, statuses[trace.EventType]));
    await db.SaveChangesAsync();
    return Results.Created($"/api/units/{unit.AtlasId}", new { unit.AtlasId });
});

api.MapGet("/units/{atlasId}", async (string atlasId, UnitAtlasDb db) =>
{
    var unit = await db.Units.AsNoTracking().Include(x => x.Product).SingleOrDefaultAsync(x => x.AtlasId == atlasId);
    if (unit is null) return Results.NotFound(new { message = "Unit not found." });
    var state = await db.UnitStates.AsNoTracking().SingleAsync(x => x.UnitId == unit.Id);
    var events = await db.TraceEvents.AsNoTracking().Where(x => x.UnitId == unit.Id)
        .OrderByDescending(x => x.OccurredAt)
        .Select(x => new { x.Id, x.EventType, x.Location, x.Actor, x.OccurredAt, x.RecordedAt })
        .ToListAsync();
    return Results.Ok(new
    {
        unit.AtlasId, unit.Serial, unit.Lot, unit.ManufacturedAt,
        product = new { unit.Product.Name, unit.Product.Sku, unit.Product.Gtin },
        state = new { state.Status, state.Location, state.UpdatedAt }, events
    });
});

api.MapPost("/units/{atlasId}/events", async (string atlasId, EventRequest request, UnitAtlasDb db) =>
{
    if (!statuses.TryGetValue(request.EventType, out var status))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["eventType"] = [$"Allowed: {string.Join(", ", statuses.Keys)}"] });
    if (string.IsNullOrWhiteSpace(request.Location) || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["event"] = ["Location and idempotencyKey are required."] });

    var unit = await db.Units.SingleOrDefaultAsync(x => x.AtlasId == atlasId);
    if (unit is null) return Results.NotFound(new { message = "Unit not found." });
    var existing = await db.TraceEvents.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == unit.TenantId && x.IdempotencyKey == request.IdempotencyKey);
    if (existing is not null) return Results.Ok(new { existing.Id, duplicate = true });

    var trace = NewEvent(unit, request.EventType.ToUpperInvariant(), request.Location.Trim(), request.IdempotencyKey.Trim(), request.Actor?.Trim() ?? "operator", request.OccurredAt ?? DateTimeOffset.UtcNow);
    var state = await db.UnitStates.SingleAsync(x => x.UnitId == unit.Id);
    state.Status = status;
    state.Location = trace.Location;
    state.LastEventId = trace.Id;
    state.UpdatedAt = trace.RecordedAt;
    db.TraceEvents.Add(trace);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
        return Results.Ok(new { trace.Id, duplicate = true });
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

static TraceEvent NewEvent(TrackedUnit unit, string type, string location, string key, string actor, DateTimeOffset occurredAt) => new()
{
    Id = Guid.NewGuid(), TenantId = unit.TenantId, UnitId = unit.Id, EventType = type,
    Location = location, Actor = actor, SourceSystem = "unitatlas", IdempotencyKey = key,
    OccurredAt = occurredAt, RecordedAt = DateTimeOffset.UtcNow
};

static UnitState NewState(TrackedUnit unit, TraceEvent trace, string status) => new()
{
    UnitId = unit.Id, TenantId = unit.TenantId, Status = status, Location = trace.Location,
    LastEventId = trace.Id, UpdatedAt = trace.RecordedAt
};

static async Task SeedAsync(UnitAtlasDb db)
{
    if (await db.Tenants.AnyAsync()) return;

    var now = DateTimeOffset.UtcNow;
    var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Atlas Manufacturing", CreatedAt = now };
    var product = new Product
    {
        Id = Guid.NewGuid(), TenantId = tenant.Id, Sku = "MOTOR-X200", Name = "Электродвигатель X200",
        Gtin = "04871234567890", CreatedAt = now
    };
    db.AddRange(tenant, product);

    var seeds = new[]
    {
        ("UA-KZ-2026-0000058219", "X200-260815-00042", "LOT-260815-A", "SHIPPED", "Distributor ABC"),
        ("UA-KZ-2026-0000058220", "X200-260815-00043", "LOT-260815-A", "PACKED", "Line 3"),
        ("UA-KZ-2026-0000058221", "X200-260815-00044", "LOT-260815-A", "QUALITY_PASSED", "QC Station 2")
    };
    foreach (var (atlasId, serial, lot, type, location) in seeds)
    {
        var unit = new TrackedUnit
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ProductId = product.Id, AtlasId = atlasId,
            Serial = serial, Lot = lot, ManufacturedAt = now.AddDays(-1), CreatedAt = now.AddDays(-1)
        };
        var manufactured = NewEvent(unit, "MANUFACTURED", "Factory #1", $"seed:{unit.Id}:manufactured", "system", unit.ManufacturedAt);
        var latest = NewEvent(unit, type, location, $"seed:{unit.Id}:{type}", "demo.operator", now.AddHours(-1));
        db.AddRange(unit, manufactured, latest, NewState(unit, latest, new Dictionary<string, string>
        {
            ["SHIPPED"] = "Shipped", ["PACKED"] = "Packed", ["QUALITY_PASSED"] = "QC passed"
        }[type]));
    }
    await db.SaveChangesAsync();
}
