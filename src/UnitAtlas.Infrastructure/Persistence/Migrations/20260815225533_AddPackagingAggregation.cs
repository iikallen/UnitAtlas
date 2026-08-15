using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnitAtlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackagingAggregation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "logistic_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Sscc = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistic_units", x => x.Id);
                    table.UniqueConstraint("AK_logistic_units_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_logistic_units_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aggregation_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentLogisticUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ActorSubject = table.Column<string>(type: "text", nullable: false),
                    SourceSystem = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    ChildrenJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReadPointId = table.Column<Guid>(type: "uuid", nullable: true),
                    BusinessLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aggregation_events", x => x.Id);
                    table.UniqueConstraint("AK_aggregation_events_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_aggregation_events_locations_TenantId_BusinessLocationId",
                        columns: x => new { x.TenantId, x.BusinessLocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aggregation_events_locations_TenantId_ReadPointId",
                        columns: x => new { x.TenantId, x.ReadPointId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aggregation_events_logistic_units_TenantId_ParentLogisticUn~",
                        columns: x => new { x.TenantId, x.ParentLogisticUnitId },
                        principalTable: "logistic_units",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aggregation_events_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "logistic_unit_contents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentLogisticUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChildLogisticUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedByEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistic_unit_contents", x => x.Id);
                    table.UniqueConstraint("AK_logistic_unit_contents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_logistic_unit_contents_exactly_one_child", "(\"ChildUnitId\" IS NOT NULL AND \"ChildLogisticUnitId\" IS NULL) OR (\"ChildUnitId\" IS NULL AND \"ChildLogisticUnitId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_logistic_unit_contents_aggregation_events_TenantId_AddedByE~",
                        columns: x => new { x.TenantId, x.AddedByEventId },
                        principalTable: "aggregation_events",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistic_unit_contents_logistic_units_TenantId_ChildLogisti~",
                        columns: x => new { x.TenantId, x.ChildLogisticUnitId },
                        principalTable: "logistic_units",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistic_unit_contents_logistic_units_TenantId_ParentLogist~",
                        columns: x => new { x.TenantId, x.ParentLogisticUnitId },
                        principalTable: "logistic_units",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistic_unit_contents_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistic_unit_contents_units_TenantId_ChildUnitId",
                        columns: x => new { x.TenantId, x.ChildUnitId },
                        principalTable: "units",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aggregation_events_TenantId_BusinessLocationId",
                table: "aggregation_events",
                columns: new[] { "TenantId", "BusinessLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_aggregation_events_TenantId_IdempotencyKey",
                table: "aggregation_events",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aggregation_events_TenantId_ParentLogisticUnitId_Sequence",
                table: "aggregation_events",
                columns: new[] { "TenantId", "ParentLogisticUnitId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aggregation_events_TenantId_ReadPointId",
                table: "aggregation_events",
                columns: new[] { "TenantId", "ReadPointId" });

            migrationBuilder.CreateIndex(
                name: "IX_logistic_unit_contents_TenantId_AddedByEventId",
                table: "logistic_unit_contents",
                columns: new[] { "TenantId", "AddedByEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_logistic_unit_contents_TenantId_ChildLogisticUnitId",
                table: "logistic_unit_contents",
                columns: new[] { "TenantId", "ChildLogisticUnitId" },
                unique: true,
                filter: "\"ChildLogisticUnitId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_logistic_unit_contents_TenantId_ChildUnitId",
                table: "logistic_unit_contents",
                columns: new[] { "TenantId", "ChildUnitId" },
                unique: true,
                filter: "\"ChildUnitId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_logistic_unit_contents_TenantId_ParentLogisticUnitId",
                table: "logistic_unit_contents",
                columns: new[] { "TenantId", "ParentLogisticUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_logistic_units_TenantId_Code",
                table: "logistic_units",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_logistic_units_TenantId_Sscc",
                table: "logistic_units",
                columns: new[] { "TenantId", "Sscc" },
                unique: true,
                filter: "\"Sscc\" IS NOT NULL");

            migrationBuilder.Sql("""
                ALTER TABLE logistic_units ENABLE ROW LEVEL SECURITY;
                ALTER TABLE logistic_units FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON logistic_units
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE logistic_unit_contents ENABLE ROW LEVEL SECURITY;
                ALTER TABLE logistic_unit_contents FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON logistic_unit_contents
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE aggregation_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE aggregation_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY aggregation_events_read ON aggregation_events FOR SELECT
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                CREATE POLICY aggregation_events_append ON aggregation_events FOR INSERT
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                CREATE TRIGGER aggregation_events_append_only
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON aggregation_events
                    FOR EACH STATEMENT EXECUTE FUNCTION unitatlas_reject_immutable_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS aggregation_events_append_only ON aggregation_events;");

            migrationBuilder.DropTable(
                name: "logistic_unit_contents");

            migrationBuilder.DropTable(
                name: "aggregation_events");

            migrationBuilder.DropTable(
                name: "logistic_units");
        }
    }
}
