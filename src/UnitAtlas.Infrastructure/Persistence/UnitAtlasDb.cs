using Microsoft.EntityFrameworkCore;
using UnitAtlas.Application.Tenancy;
using UnitAtlas.Domain;

namespace UnitAtlas.Infrastructure.Persistence;

public sealed class UnitAtlasDb(DbContextOptions<UnitAtlasDb> options, ITenantContext tenantContext) : DbContext(options)
{
    private Guid CurrentTenantId => tenantContext.IsAvailable ? tenantContext.TenantId : Guid.Empty;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<TrackedUnit> Units => Set<TrackedUnit>();
    public DbSet<TraceEvent> TraceEvents => Set<TraceEvent>();
    public DbSet<UnitState> UnitStates => Set<UnitState>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<ProductIdentifier> ProductIdentifiers => Set<ProductIdentifier>();
    public DbSet<UnitIdentifier> UnitIdentifiers => Set<UnitIdentifier>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<PublicPassportConfig> PublicPassportConfigs => Set<PublicPassportConfig>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ExternalReference> ExternalReferences => Set<ExternalReference>();
    public DbSet<IntegrationEndpoint> IntegrationEndpoints => Set<IntegrationEndpoint>();
    public DbSet<IntegrationDelivery> IntegrationDeliveries => Set<IntegrationDelivery>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<LogisticUnit> LogisticUnits => Set<LogisticUnit>();
    public DbSet<AggregationEvent> AggregationEvents => Set<AggregationEvent>();
    public DbSet<LogisticUnitContent> LogisticUnitContents => Set<LogisticUnitContent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Tenant>().ToTable("tenants");
        model.Entity<Tenant>().Property(x => x.RegulatoryGatewayMode).HasDefaultValue("NONE");

        model.Entity<TenantMembership>().ToTable("tenant_memberships");
        model.Entity<TenantMembership>().Property(x => x.Role).HasConversion<string>();
        model.Entity<TenantMembership>().HasIndex(x => new { x.TenantId, x.UserSubject }).IsUnique();
        model.Entity<TenantMembership>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        model.Entity<TenantMembership>().HasQueryFilter(x => x.TenantId == CurrentTenantId);

        model.Entity<Product>().ToTable("products");
        model.Entity<Product>().HasAlternateKey(x => new { x.TenantId, x.Id });
        model.Entity<Product>().HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        model.Entity<Product>().HasIndex(x => new { x.TenantId, x.Gtin }).IsUnique();
        model.Entity<Product>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<Product>().HasQueryFilter(x => x.TenantId == CurrentTenantId);

        model.Entity<TrackedUnit>().ToTable("units");
        model.Entity<TrackedUnit>().HasAlternateKey(x => new { x.TenantId, x.Id });
        model.Entity<TrackedUnit>().HasIndex(x => new { x.TenantId, x.AtlasId }).IsUnique();
        model.Entity<TrackedUnit>().HasIndex(x => new { x.TenantId, x.Serial }).IsUnique();
        model.Entity<TrackedUnit>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<TrackedUnit>().HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });
        model.Entity<TrackedUnit>().HasQueryFilter(x => x.TenantId == CurrentTenantId);

        model.Entity<TraceEvent>().ToTable("trace_events");
        model.Entity<TraceEvent>().HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        model.Entity<TraceEvent>().HasIndex(x => new { x.TenantId, x.UnitId, x.Sequence }).IsUnique();
        model.Entity<TraceEvent>().HasIndex(x => new { x.TenantId, x.UnitId, x.OccurredAt, x.Sequence });
        model.Entity<TraceEvent>().Property(x => x.MetadataJson).HasColumnType("jsonb");
        model.Entity<TraceEvent>().HasOne<TrackedUnit>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });
        model.Entity<TraceEvent>().HasOne<Location>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ReadPointId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<TraceEvent>().HasOne<Location>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BusinessLocationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<TraceEvent>().HasQueryFilter(x => x.TenantId == CurrentTenantId);

        model.Entity<UnitState>().ToTable("unit_states").HasKey(x => x.UnitId);
        model.Entity<UnitState>().HasOne<TrackedUnit>().WithOne()
            .HasForeignKey<UnitState>(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey<TrackedUnit>(x => new { x.TenantId, x.Id });
        model.Entity<UnitState>().HasQueryFilter(x => x.TenantId == CurrentTenantId);

        TenantEntity<Site>(model, "sites");
        model.Entity<Site>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

        TenantEntity<Location>(model, "locations");
        model.Entity<Location>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        model.Entity<Location>().HasOne<Site>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SiteId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });
        model.Entity<Location>().HasOne<Location>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParentLocationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        TenantEntity<Lot>(model, "lots");
        model.Entity<Lot>().HasIndex(x => new { x.TenantId, x.ProductId, x.Code }).IsUnique();
        model.Entity<Lot>().HasOne<Product>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });
        model.Entity<TrackedUnit>().HasOne<Lot>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.LotId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        TenantEntity<ProductIdentifier>(model, "product_identifiers");
        model.Entity<ProductIdentifier>().HasIndex(x => new { x.TenantId, x.Type, x.Value }).IsUnique();
        model.Entity<ProductIdentifier>().HasOne<Product>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });

        TenantEntity<UnitIdentifier>(model, "unit_identifiers");
        model.Entity<UnitIdentifier>().HasIndex(x => new { x.TenantId, x.Type, x.Value }).IsUnique();
        model.Entity<UnitIdentifier>().HasOne<TrackedUnit>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });

        TenantEntity<IdempotencyRecord>(model, "idempotency_records");
        model.Entity<IdempotencyRecord>().HasIndex(x => new { x.TenantId, x.Key }).IsUnique();

        TenantEntity<AuditEntry>(model, "audit_entries");
        model.Entity<AuditEntry>().Property(x => x.DataJson).HasColumnType("jsonb");

        model.Entity<PublicPassportConfig>().ToTable("public_passport_configs").HasKey(x => x.UnitId);
        model.Entity<PublicPassportConfig>().HasIndex(x => x.PublicId).IsUnique();
        model.Entity<PublicPassportConfig>().HasOne<TrackedUnit>().WithOne()
            .HasForeignKey<PublicPassportConfig>(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey<TrackedUnit>(x => new { x.TenantId, x.Id });

        TenantEntity<OutboxMessage>(model, "outbox_messages");
        model.Entity<OutboxMessage>().Property(x => x.PayloadJson).HasColumnType("jsonb");
        model.Entity<OutboxMessage>().HasIndex(x => new { x.TenantId, x.CreatedAt });

        TenantEntity<ExternalReference>(model, "external_references");
        model.Entity<ExternalReference>().HasIndex(x => new { x.TenantId, x.System, x.EntityType, x.Value }).IsUnique();

        TenantEntity<IntegrationEndpoint>(model, "integration_endpoints");
        model.Entity<IntegrationEndpoint>().Property(x => x.SettingsJson).HasColumnType("jsonb");
        model.Entity<IntegrationEndpoint>().HasIndex(x => new { x.TenantId, x.System }).IsUnique();

        TenantEntity<IntegrationDelivery>(model, "integration_deliveries");
        model.Entity<IntegrationDelivery>().Property(x => x.Status).HasConversion<string>();
        model.Entity<IntegrationDelivery>().HasIndex(x => new { x.TenantId, x.OutboxMessageId, x.IntegrationEndpointId }).IsUnique();
        model.Entity<IntegrationDelivery>().HasIndex(x => new { x.TenantId, x.Status, x.NextAttemptAt });
        model.Entity<IntegrationDelivery>().HasOne<OutboxMessage>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OutboxMessageId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<IntegrationDelivery>().HasOne<IntegrationEndpoint>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.IntegrationEndpointId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        TenantEntity<InboxMessage>(model, "inbox_messages");
        model.Entity<InboxMessage>().Property(x => x.PayloadJson).HasColumnType("jsonb");
        model.Entity<InboxMessage>().Property(x => x.ResultJson).HasColumnType("jsonb");
        model.Entity<InboxMessage>().HasIndex(x => new { x.TenantId, x.SourceSystem, x.ExternalMessageId }).IsUnique();
        model.Entity<InboxMessage>().HasOne<IntegrationEndpoint>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.IntegrationEndpointId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        TenantEntity<LogisticUnit>(model, "logistic_units");
        model.Entity<LogisticUnit>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        model.Entity<LogisticUnit>().HasIndex(x => new { x.TenantId, x.Sscc }).IsUnique().HasFilter("\"Sscc\" IS NOT NULL");

        TenantEntity<AggregationEvent>(model, "aggregation_events");
        model.Entity<AggregationEvent>().Property(x => x.ChildrenJson).HasColumnType("jsonb");
        model.Entity<AggregationEvent>().HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        model.Entity<AggregationEvent>().HasIndex(x => new { x.TenantId, x.ParentLogisticUnitId, x.Sequence }).IsUnique();
        model.Entity<AggregationEvent>().HasOne<LogisticUnit>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParentLogisticUnitId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<AggregationEvent>().HasOne<Location>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ReadPointId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<AggregationEvent>().HasOne<Location>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BusinessLocationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        TenantEntity<LogisticUnitContent>(model, "logistic_unit_contents");
        model.Entity<LogisticUnitContent>().ToTable("logistic_unit_contents", table => table.HasCheckConstraint(
            "CK_logistic_unit_contents_exactly_one_child",
            "(\"ChildUnitId\" IS NOT NULL AND \"ChildLogisticUnitId\" IS NULL) OR (\"ChildUnitId\" IS NULL AND \"ChildLogisticUnitId\" IS NOT NULL)"));
        model.Entity<LogisticUnitContent>().HasIndex(x => new { x.TenantId, x.ChildUnitId }).IsUnique().HasFilter("\"ChildUnitId\" IS NOT NULL");
        model.Entity<LogisticUnitContent>().HasIndex(x => new { x.TenantId, x.ChildLogisticUnitId }).IsUnique().HasFilter("\"ChildLogisticUnitId\" IS NOT NULL");
        model.Entity<LogisticUnitContent>().HasOne<LogisticUnit>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParentLogisticUnitId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<LogisticUnitContent>().HasOne<TrackedUnit>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ChildUnitId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<LogisticUnitContent>().HasOne<LogisticUnit>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ChildLogisticUnitId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<LogisticUnitContent>().HasOne<AggregationEvent>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AddedByEventId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private void TenantEntity<TEntity>(ModelBuilder model, string table) where TEntity : class
    {
        model.Entity<TEntity>().ToTable(table);
        model.Entity<TEntity>().HasAlternateKey("TenantId", "Id");
        model.Entity<TEntity>().HasOne<Tenant>().WithMany()
            .HasForeignKey("TenantId")
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<TEntity>().HasQueryFilter(BuildTenantFilter<TEntity>());
    }

    private System.Linq.Expressions.Expression<Func<TEntity, bool>> BuildTenantFilter<TEntity>() where TEntity : class =>
        entity => EF.Property<Guid>(entity, "TenantId") == CurrentTenantId;
}
