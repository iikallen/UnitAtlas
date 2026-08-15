using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnitAtlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Gtin = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                    table.UniqueConstraint("AK_products_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_products_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtlasId = table.Column<string>(type: "text", nullable: false),
                    Serial = table.Column<string>(type: "text", nullable: false),
                    Lot = table.Column<string>(type: "text", nullable: false),
                    ManufacturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units", x => x.Id);
                    table.UniqueConstraint("AK_units_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_units_products_TenantId_ProductId",
                        columns: x => new { x.TenantId, x.ProductId },
                        principalTable: "products",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_units_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trace_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    SourceSystem = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trace_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trace_events_units_TenantId_UnitId",
                        columns: x => new { x.TenantId, x.UnitId },
                        principalTable: "units",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "unit_states",
                columns: table => new
                {
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: false),
                    LastEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentOccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentSequence = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unit_states", x => x.UnitId);
                    table.ForeignKey(
                        name: "FK_unit_states_units_TenantId_UnitId",
                        columns: x => new { x.TenantId, x.UnitId },
                        principalTable: "units",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_Gtin",
                table: "products",
                columns: new[] { "TenantId", "Gtin" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_Sku",
                table: "products",
                columns: new[] { "TenantId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_TenantId_IdempotencyKey",
                table: "trace_events",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_TenantId_UnitId_OccurredAt_Sequence",
                table: "trace_events",
                columns: new[] { "TenantId", "UnitId", "OccurredAt", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_TenantId_UnitId_Sequence",
                table: "trace_events",
                columns: new[] { "TenantId", "UnitId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_unit_states_TenantId_UnitId",
                table: "unit_states",
                columns: new[] { "TenantId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_units_TenantId_AtlasId",
                table: "units",
                columns: new[] { "TenantId", "AtlasId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_units_TenantId_ProductId",
                table: "units",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_units_TenantId_Serial",
                table: "units",
                columns: new[] { "TenantId", "Serial" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trace_events");

            migrationBuilder.DropTable(
                name: "unit_states");

            migrationBuilder.DropTable(
                name: "units");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
