using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnitAtlas.Application.Traceability;
using UnitAtlas.Domain;

namespace UnitAtlas.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UnitAtlasDb>();
        await db.Database.MigrateAsync(cancellationToken);

        if (bool.TryParse(configuration["Demo:SeedData"], out var seedData) && seedData)
            await SeedAsync(db, cancellationToken);
    }

    private static async Task SeedAsync(UnitAtlasDb db, CancellationToken cancellationToken)
    {
        if (await db.Tenants.AnyAsync(cancellationToken)) return;

        var now = DateTimeOffset.UtcNow;
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Atlas Manufacturing", CreatedAt = now };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Sku = "MOTOR-X200",
            Name = "Электродвигатель X200",
            Gtin = "04871234567890",
            CreatedAt = now
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
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                ProductId = product.Id,
                AtlasId = atlasId,
                Serial = serial,
                Lot = lot,
                ManufacturedAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-1)
            };
            var manufactured = NewEvent(unit, "MANUFACTURED", "Factory #1", $"seed:{unit.Id}:manufactured", "system", unit.ManufacturedAt, 1);
            var latest = NewEvent(unit, type, location, $"seed:{unit.Id}:{type}", "demo.operator", now.AddHours(-1), 2);
            TraceEventProjection.TryGetStatus(type, out var status);
            db.AddRange(unit, manufactured, latest, NewState(unit, latest, status));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static TraceEvent NewEvent(TrackedUnit unit, string type, string location, string key, string actor, DateTimeOffset occurredAt, long sequence) => new()
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

    private static UnitState NewState(TrackedUnit unit, TraceEvent trace, string status) => new()
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
}
