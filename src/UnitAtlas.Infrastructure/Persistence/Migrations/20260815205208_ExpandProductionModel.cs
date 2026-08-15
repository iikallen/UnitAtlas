using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnitAtlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandProductionModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LotId",
                table: "units",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorSubject",
                table: "trace_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessLocationId",
                table: "trace_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessStep",
                table: "trace_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "trace_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Disposition",
                table: "trace_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "trace_events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReadPointId",
                table: "trace_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorSubject = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                    table.UniqueConstraint("AK_audit_entries_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_audit_entries_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_references",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    System = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_references", x => x.Id);
                    table.UniqueConstraint("AK_external_references_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_external_references_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    RequestHash = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.Id);
                    table.UniqueConstraint("AK_idempotency_records_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_idempotency_records_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    ManufacturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lots", x => x.Id);
                    table.UniqueConstraint("AK_lots_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_lots_products_TenantId_ProductId",
                        columns: x => new { x.TenantId, x.ProductId },
                        principalTable: "products",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lots_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                    table.UniqueConstraint("AK_outbox_messages_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_outbox_messages_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_identifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_identifiers", x => x.Id);
                    table.UniqueConstraint("AK_product_identifiers_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_product_identifiers_products_TenantId_ProductId",
                        columns: x => new { x.TenantId, x.ProductId },
                        principalTable: "products",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_identifiers_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "public_passport_configs",
                columns: table => new
                {
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicId = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_passport_configs", x => x.UnitId);
                    table.ForeignKey(
                        name: "FK_public_passport_configs_units_TenantId_UnitId",
                        columns: x => new { x.TenantId, x.UnitId },
                        principalTable: "units",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sites", x => x.Id);
                    table.UniqueConstraint("AK_sites_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_sites_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unit_identifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unit_identifiers", x => x.Id);
                    table.UniqueConstraint("AK_unit_identifiers_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_unit_identifiers_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_unit_identifiers_units_TenantId_UnitId",
                        columns: x => new { x.TenantId, x.UnitId },
                        principalTable: "units",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locations", x => x.Id);
                    table.UniqueConstraint("AK_locations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_locations_locations_TenantId_ParentLocationId",
                        columns: x => new { x.TenantId, x.ParentLocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_locations_sites_TenantId_SiteId",
                        columns: x => new { x.TenantId, x.SiteId },
                        principalTable: "sites",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_locations_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_units_TenantId_LotId",
                table: "units",
                columns: new[] { "TenantId", "LotId" });

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_TenantId_BusinessLocationId",
                table: "trace_events",
                columns: new[] { "TenantId", "BusinessLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_TenantId_ReadPointId",
                table: "trace_events",
                columns: new[] { "TenantId", "ReadPointId" });

            migrationBuilder.CreateIndex(
                name: "IX_external_references_TenantId_System_EntityType_Value",
                table: "external_references",
                columns: new[] { "TenantId", "System", "EntityType", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_TenantId_Key",
                table: "idempotency_records",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_locations_TenantId_Code",
                table: "locations",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_locations_TenantId_ParentLocationId",
                table: "locations",
                columns: new[] { "TenantId", "ParentLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_TenantId_SiteId",
                table: "locations",
                columns: new[] { "TenantId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_lots_TenantId_ProductId_Code",
                table: "lots",
                columns: new[] { "TenantId", "ProductId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_TenantId_ProcessedAt_CreatedAt",
                table: "outbox_messages",
                columns: new[] { "TenantId", "ProcessedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_product_identifiers_TenantId_ProductId",
                table: "product_identifiers",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_product_identifiers_TenantId_Type_Value",
                table: "product_identifiers",
                columns: new[] { "TenantId", "Type", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_public_passport_configs_PublicId",
                table: "public_passport_configs",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_public_passport_configs_TenantId_UnitId",
                table: "public_passport_configs",
                columns: new[] { "TenantId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sites_TenantId_Code",
                table: "sites",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_unit_identifiers_TenantId_Type_Value",
                table: "unit_identifiers",
                columns: new[] { "TenantId", "Type", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_unit_identifiers_TenantId_UnitId",
                table: "unit_identifiers",
                columns: new[] { "TenantId", "UnitId" });

            migrationBuilder.AddForeignKey(
                name: "FK_trace_events_locations_TenantId_BusinessLocationId",
                table: "trace_events",
                columns: new[] { "TenantId", "BusinessLocationId" },
                principalTable: "locations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trace_events_locations_TenantId_ReadPointId",
                table: "trace_events",
                columns: new[] { "TenantId", "ReadPointId" },
                principalTable: "locations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_units_lots_TenantId_LotId",
                table: "units",
                columns: new[] { "TenantId", "LotId" },
                principalTable: "lots",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                ALTER TABLE products NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE products DISABLE ROW LEVEL SECURITY;
                ALTER TABLE units NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE units DISABLE ROW LEVEL SECURITY;

                INSERT INTO product_identifiers ("Id", "TenantId", "ProductId", "Type", "Value")
                SELECT gen_random_uuid(), "TenantId", "Id", 'GTIN', "Gtin" FROM products
                UNION ALL
                SELECT gen_random_uuid(), "TenantId", "Id", 'SKU', "Sku" FROM products;

                INSERT INTO unit_identifiers ("Id", "TenantId", "UnitId", "Type", "Value")
                SELECT gen_random_uuid(), "TenantId", "Id", 'ATLAS_ID', "AtlasId" FROM units
                UNION ALL
                SELECT gen_random_uuid(), "TenantId", "Id", 'SERIAL', "Serial" FROM units;

                INSERT INTO lots ("Id", "TenantId", "ProductId", "Code", "ManufacturedAt", "ExpiresAt")
                SELECT gen_random_uuid(), "TenantId", "ProductId", "Lot", min("ManufacturedAt"), NULL
                FROM units GROUP BY "TenantId", "ProductId", "Lot";

                UPDATE units AS unit
                SET "LotId" = lot."Id"
                FROM lots AS lot
                WHERE lot."TenantId" = unit."TenantId"
                  AND lot."ProductId" = unit."ProductId"
                  AND lot."Code" = unit."Lot";

                ALTER TABLE products ENABLE ROW LEVEL SECURITY;
                ALTER TABLE products FORCE ROW LEVEL SECURITY;
                ALTER TABLE units ENABLE ROW LEVEL SECURITY;
                ALTER TABLE units FORCE ROW LEVEL SECURITY;

                DO $policy$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY[
                        'audit_entries', 'external_references', 'idempotency_records', 'locations', 'lots',
                        'outbox_messages', 'product_identifiers', 'public_passport_configs', 'sites', 'unit_identifiers'
                    ] LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', table_name);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I USING ("TenantId" = NULLIF(current_setting(''app.current_tenant'', true), '''')::uuid) WITH CHECK ("TenantId" = NULLIF(current_setting(''app.current_tenant'', true), '''')::uuid)',
                            table_name);
                    END LOOP;
                END
                $policy$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_trace_events_locations_TenantId_BusinessLocationId",
                table: "trace_events");

            migrationBuilder.DropForeignKey(
                name: "FK_trace_events_locations_TenantId_ReadPointId",
                table: "trace_events");

            migrationBuilder.DropForeignKey(
                name: "FK_units_lots_TenantId_LotId",
                table: "units");

            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "external_references");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "lots");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "product_identifiers");

            migrationBuilder.DropTable(
                name: "public_passport_configs");

            migrationBuilder.DropTable(
                name: "unit_identifiers");

            migrationBuilder.DropTable(
                name: "sites");

            migrationBuilder.DropIndex(
                name: "IX_units_TenantId_LotId",
                table: "units");

            migrationBuilder.DropIndex(
                name: "IX_trace_events_TenantId_BusinessLocationId",
                table: "trace_events");

            migrationBuilder.DropIndex(
                name: "IX_trace_events_TenantId_ReadPointId",
                table: "trace_events");

            migrationBuilder.DropColumn(
                name: "LotId",
                table: "units");

            migrationBuilder.DropColumn(
                name: "ActorSubject",
                table: "trace_events");

            migrationBuilder.DropColumn(
                name: "BusinessLocationId",
                table: "trace_events");

            migrationBuilder.DropColumn(
                name: "BusinessStep",
                table: "trace_events");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "trace_events");

            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "trace_events");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "trace_events");

            migrationBuilder.DropColumn(
                name: "ReadPointId",
                table: "trace_events");
        }
    }
}
