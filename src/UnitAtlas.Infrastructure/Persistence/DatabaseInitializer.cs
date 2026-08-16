using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnitAtlas.Application.Tenancy;
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
            await SeedAsync(db, scope.ServiceProvider.GetRequiredService<ITenantContext>(), cancellationToken);
    }

    private static async Task SeedAsync(UnitAtlasDb db, ITenantContext tenantContext, CancellationToken cancellationToken)
    {
        if (await db.Tenants.AnyAsync(cancellationToken)) return;

        var now = DateTimeOffset.UtcNow;
        var tenant = new Tenant { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Atlas Manufacturing", RegulatoryGatewayMode = "NONE", CreatedAt = now };
        tenantContext.Initialize(tenant.Id, "demo.operator", TenantRole.Owner);
        var membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserSubject = "demo.operator",
            Role = TenantRole.Owner,
            CreatedAt = now
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Sku = "MOTOR-X200",
            Name = "Электродвигатель X200",
            Gtin = "04871234567890",
            CreatedAt = now
        };
        var seedLot = new Lot
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProductId = product.Id,
            Code = "LOT-260815-A",
            ManufacturedAt = now.AddDays(-1)
        };
        var site = new Site { Id = Guid.NewGuid(), TenantId = tenant.Id, Code = "FACTORY-1", Name = "Almaty Factory" };
        var seedLocation = new Location
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            SiteId = site.Id,
            Code = "LINE-3",
            Name = "Production Line 3",
            Type = "production_line"
        };
        db.AddRange(tenant, membership, product, seedLot, site, seedLocation,
            new ProductIdentifier { Id = Guid.NewGuid(), TenantId = tenant.Id, ProductId = product.Id, Type = "GTIN", Value = product.Gtin },
            new ProductIdentifier { Id = Guid.NewGuid(), TenantId = tenant.Id, ProductId = product.Id, Type = "SKU", Value = product.Sku },
            new LabelTemplate { Id = Guid.NewGuid(), TenantId = tenant.Id, Code = "INTERNAL_UNIT_QR", EntityType = "UNIT", IdentifierMode = "INTERNAL", Symbology = "QR", CreatedAt = now },
            new LabelTemplate { Id = Guid.NewGuid(), TenantId = tenant.Id, Code = "GS1_DATAMATRIX_UNIT", EntityType = "UNIT", IdentifierMode = "GS1", Symbology = "GS1_DATA_MATRIX", CreatedAt = now },
            new LabelTemplate { Id = Guid.NewGuid(), TenantId = tenant.Id, Code = "INTERNAL_LOGISTICS_QR", EntityType = "LOGISTIC_UNIT", IdentifierMode = "INTERNAL", Symbology = "QR", CreatedAt = now },
            new LabelTemplate { Id = Guid.NewGuid(), TenantId = tenant.Id, Code = "GS1_LOGISTICS_LABEL", EntityType = "LOGISTIC_UNIT", IdentifierMode = "GS1", Symbology = "GS1_128", CreatedAt = now },
            new PrintProfile { Id = Guid.NewGuid(), TenantId = tenant.Id, Code = "DEMO-INTERNAL", IdentifierMode = "INTERNAL", CreatedAt = now },
            new Printer { Id = Guid.NewGuid(), TenantId = tenant.Id, Code = "DEMO-EDGE", Name = "Demo edge printer", Transport = "EDGE", Endpoint = "station://demo", IsEnabled = true, CreatedAt = now });

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
                LotId = seedLot.Id,
                ManufacturedAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-1)
            };
            var manufactured = NewEvent(unit, "MANUFACTURED", "Factory #1", $"seed:{unit.Id}:manufactured", "system", unit.ManufacturedAt, 1);
            var latest = NewEvent(unit, type, location, $"seed:{unit.Id}:{type}", "demo.operator", now.AddHours(-1), 2);
            manufactured.ReadPointId = seedLocation.Id;
            latest.BusinessLocationId = seedLocation.Id;
            TraceEventProjection.TryGetStatus(type, out var status);
            db.AddRange(unit, manufactured, latest, NewState(unit, latest, status),
                new UnitIdentifier { Id = Guid.NewGuid(), TenantId = tenant.Id, UnitId = unit.Id, Type = "ATLAS_ID", Value = unit.AtlasId },
                new UnitIdentifier { Id = Guid.NewGuid(), TenantId = tenant.Id, UnitId = unit.Id, Type = "SERIAL", Value = unit.Serial },
                new PublicPassportConfig
                {
                    UnitId = unit.Id,
                    TenantId = tenant.Id,
                    PublicId = atlasId == "UA-KZ-2026-0000058219" ? "demo-x200-58219" : Guid.NewGuid().ToString("N"),
                    IsPublished = atlasId == "UA-KZ-2026-0000058219"
                });
        }
        await db.SaveChangesAsync(cancellationToken);

        var secondTenant = new Tenant { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Second Tenant", RegulatoryGatewayMode = "NONE", CreatedAt = now };
        var secondMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = secondTenant.Id,
            UserSubject = "second.viewer",
            Role = TenantRole.Viewer,
            CreatedAt = now
        };
        var secondProduct = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = secondTenant.Id,
            Sku = "PRIVATE-SKU",
            Name = "Second tenant product",
            Gtin = "04870000000001",
            CreatedAt = now
        };
        var secondLot = new Lot
        {
            Id = Guid.NewGuid(),
            TenantId = secondTenant.Id,
            ProductId = secondProduct.Id,
            Code = "PRIVATE-LOT",
            ManufacturedAt = now.AddDays(-1)
        };
        tenantContext.Initialize(secondTenant.Id, "second.viewer", TenantRole.Viewer);
        var secondUnit = new TrackedUnit
        {
            Id = Guid.NewGuid(),
            TenantId = secondTenant.Id,
            ProductId = secondProduct.Id,
            AtlasId = "UA-KZ-2026-PRIVATE0001",
            Serial = "PRIVATE-0001",
            Lot = "PRIVATE-LOT",
            LotId = secondLot.Id,
            ManufacturedAt = now.AddDays(-1),
            CreatedAt = now.AddDays(-1)
        };
        var secondEvent = NewEvent(secondUnit, "MANUFACTURED", "Private factory", $"seed:{secondUnit.Id}:manufactured", "system", secondUnit.ManufacturedAt, 1);
        db.AddRange(secondTenant, secondMembership, secondProduct, secondLot, secondUnit, secondEvent, NewState(secondUnit, secondEvent, "Manufactured"),
            new ProductIdentifier { Id = Guid.NewGuid(), TenantId = secondTenant.Id, ProductId = secondProduct.Id, Type = "GTIN", Value = secondProduct.Gtin },
            new UnitIdentifier { Id = Guid.NewGuid(), TenantId = secondTenant.Id, UnitId = secondUnit.Id, Type = "ATLAS_ID", Value = secondUnit.AtlasId });
        await db.SaveChangesAsync(cancellationToken);
        tenantContext.Clear();
    }

    private static TraceEvent NewEvent(TrackedUnit unit, string type, string location, string key, string actor, DateTimeOffset occurredAt, long sequence) => new()
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
