using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UnitAtlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStationDeviceManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_TenantId_CreatedAt",
                table: "outbox_messages");

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                table: "trace_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StationId",
                table: "trace_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "outbox_messages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                table: "aggregation_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StationId",
                table: "aggregation_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.Id);
                    table.UniqueConstraint("AK_devices_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_devices_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadPointId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stations", x => x.Id);
                    table.UniqueConstraint("AK_stations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_stations_locations_TenantId_BusinessLocationId",
                        columns: x => new { x.TenantId, x.BusinessLocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stations_locations_TenantId_ReadPointId",
                        columns: x => new { x.TenantId, x.ReadPointId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stations_sites_TenantId_SiteId",
                        columns: x => new { x.TenantId, x.SiteId },
                        principalTable: "sites",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stations_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentCodeHash = table.Column<string>(type: "text", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_enrollments", x => x.Id);
                    table.UniqueConstraint("AK_device_enrollments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_device_enrollments_devices_TenantId_DeviceId",
                        columns: x => new { x.TenantId, x.DeviceId },
                        principalTable: "devices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_enrollments_stations_TenantId_StationId",
                        columns: x => new { x.TenantId, x.StationId },
                        principalTable: "stations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_enrollments_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSubject = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_sessions", x => x.Id);
                    table.UniqueConstraint("AK_device_sessions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_device_sessions_devices_TenantId_DeviceId",
                        columns: x => new { x.TenantId, x.DeviceId },
                        principalTable: "devices",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_sessions_stations_TenantId_StationId",
                        columns: x => new { x.TenantId, x.StationId },
                        principalTable: "stations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_sessions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_TenantId_DeviceId",
                table: "trace_events",
                columns: new[] { "TenantId", "DeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_trace_events_TenantId_StationId",
                table: "trace_events",
                columns: new[] { "TenantId", "StationId" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_TenantId_Sequence",
                table: "outbox_messages",
                columns: new[] { "TenantId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aggregation_events_TenantId_DeviceId",
                table: "aggregation_events",
                columns: new[] { "TenantId", "DeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_aggregation_events_TenantId_StationId",
                table: "aggregation_events",
                columns: new[] { "TenantId", "StationId" });

            migrationBuilder.CreateIndex(
                name: "IX_device_enrollments_TenantId_DeviceId",
                table: "device_enrollments",
                columns: new[] { "TenantId", "DeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_device_enrollments_TenantId_EnrollmentCodeHash",
                table: "device_enrollments",
                columns: new[] { "TenantId", "EnrollmentCodeHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_enrollments_TenantId_StationId",
                table: "device_enrollments",
                columns: new[] { "TenantId", "StationId" });

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_TenantId_DeviceId_RevokedAt",
                table: "device_sessions",
                columns: new[] { "TenantId", "DeviceId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_TenantId_StationId",
                table: "device_sessions",
                columns: new[] { "TenantId", "StationId" });

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_TenantId_TokenHash",
                table: "device_sessions",
                columns: new[] { "TenantId", "TokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_devices_TenantId_Code",
                table: "devices",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stations_TenantId_BusinessLocationId",
                table: "stations",
                columns: new[] { "TenantId", "BusinessLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_stations_TenantId_Code",
                table: "stations",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stations_TenantId_ReadPointId",
                table: "stations",
                columns: new[] { "TenantId", "ReadPointId" });

            migrationBuilder.CreateIndex(
                name: "IX_stations_TenantId_SiteId",
                table: "stations",
                columns: new[] { "TenantId", "SiteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_aggregation_events_devices_TenantId_DeviceId",
                table: "aggregation_events",
                columns: new[] { "TenantId", "DeviceId" },
                principalTable: "devices",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_aggregation_events_stations_TenantId_StationId",
                table: "aggregation_events",
                columns: new[] { "TenantId", "StationId" },
                principalTable: "stations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trace_events_devices_TenantId_DeviceId",
                table: "trace_events",
                columns: new[] { "TenantId", "DeviceId" },
                principalTable: "devices",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trace_events_stations_TenantId_StationId",
                table: "trace_events",
                columns: new[] { "TenantId", "StationId" },
                principalTable: "stations",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                ALTER TABLE devices ENABLE ROW LEVEL SECURITY;
                ALTER TABLE devices FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON devices
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE stations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE stations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON stations
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE device_enrollments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE device_enrollments FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON device_enrollments
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE device_sessions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE device_sessions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON device_sessions
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aggregation_events_devices_TenantId_DeviceId",
                table: "aggregation_events");

            migrationBuilder.DropForeignKey(
                name: "FK_aggregation_events_stations_TenantId_StationId",
                table: "aggregation_events");

            migrationBuilder.DropForeignKey(
                name: "FK_trace_events_devices_TenantId_DeviceId",
                table: "trace_events");

            migrationBuilder.DropForeignKey(
                name: "FK_trace_events_stations_TenantId_StationId",
                table: "trace_events");

            migrationBuilder.DropTable(
                name: "device_enrollments");

            migrationBuilder.DropTable(
                name: "device_sessions");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "stations");

            migrationBuilder.DropIndex(
                name: "IX_trace_events_TenantId_DeviceId",
                table: "trace_events");

            migrationBuilder.DropIndex(
                name: "IX_trace_events_TenantId_StationId",
                table: "trace_events");

            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_TenantId_Sequence",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_aggregation_events_TenantId_DeviceId",
                table: "aggregation_events");

            migrationBuilder.DropIndex(
                name: "IX_aggregation_events_TenantId_StationId",
                table: "aggregation_events");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "trace_events");

            migrationBuilder.DropColumn(
                name: "StationId",
                table: "trace_events");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "aggregation_events");

            migrationBuilder.DropColumn(
                name: "StationId",
                table: "aggregation_events");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_TenantId_CreatedAt",
                table: "outbox_messages",
                columns: new[] { "TenantId", "CreatedAt" });
        }
    }
}
