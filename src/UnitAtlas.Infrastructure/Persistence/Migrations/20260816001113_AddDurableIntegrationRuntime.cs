using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnitAtlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableIntegrationRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_TenantId_ProcessedAt_CreatedAt",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "outbox_messages");

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "outbox_messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "outbox_messages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubjectId",
                table: "outbox_messages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubjectType",
                table: "outbox_messages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE outbox_messages
                SET "CorrelationId" = "Id", "Source" = 'unitatlas',
                    "SubjectType" = 'LegacyEvent', "SubjectId" = "Id"::text;
                ALTER TABLE outbox_messages ALTER COLUMN "CorrelationId" DROP DEFAULT;
                ALTER TABLE outbox_messages ALTER COLUMN "Source" DROP DEFAULT;
                ALTER TABLE outbox_messages ALTER COLUMN "SubjectType" DROP DEFAULT;
                ALTER TABLE outbox_messages ALTER COLUMN "SubjectId" DROP DEFAULT;
                """);

            migrationBuilder.CreateTable(
                name: "integration_endpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    System = table.Column<string>(type: "text", nullable: false),
                    Adapter = table.Column<string>(type: "text", nullable: false),
                    BaseAddress = table.Column<string>(type: "text", nullable: false),
                    SecretRef = table.Column<string>(type: "text", nullable: true),
                    SettingsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_endpoints", x => x.Id);
                    table.UniqueConstraint("AK_integration_endpoints_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_integration_endpoints_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationEndpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "text", nullable: false),
                    ExternalMessageId = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => x.Id);
                    table.UniqueConstraint("AK_inbox_messages_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_inbox_messages_integration_endpoints_TenantId_IntegrationEn~",
                        columns: x => new { x.TenantId, x.IntegrationEndpointId },
                        principalTable: "integration_endpoints",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inbox_messages_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "integration_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutboxMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationEndpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_deliveries", x => x.Id);
                    table.UniqueConstraint("AK_integration_deliveries_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_integration_deliveries_integration_endpoints_TenantId_Integ~",
                        columns: x => new { x.TenantId, x.IntegrationEndpointId },
                        principalTable: "integration_endpoints",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_integration_deliveries_outbox_messages_TenantId_OutboxMessa~",
                        columns: x => new { x.TenantId, x.OutboxMessageId },
                        principalTable: "outbox_messages",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_integration_deliveries_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_TenantId_CreatedAt",
                table: "outbox_messages",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_TenantId_IntegrationEndpointId",
                table: "inbox_messages",
                columns: new[] { "TenantId", "IntegrationEndpointId" });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_TenantId_SourceSystem_ExternalMessageId",
                table: "inbox_messages",
                columns: new[] { "TenantId", "SourceSystem", "ExternalMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_deliveries_TenantId_IntegrationEndpointId",
                table: "integration_deliveries",
                columns: new[] { "TenantId", "IntegrationEndpointId" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_deliveries_TenantId_OutboxMessageId_Integration~",
                table: "integration_deliveries",
                columns: new[] { "TenantId", "OutboxMessageId", "IntegrationEndpointId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_deliveries_TenantId_Status_NextAttemptAt",
                table: "integration_deliveries",
                columns: new[] { "TenantId", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_endpoints_TenantId_System",
                table: "integration_endpoints",
                columns: new[] { "TenantId", "System" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE integration_endpoints ENABLE ROW LEVEL SECURITY;
                ALTER TABLE integration_endpoints FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON integration_endpoints
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE integration_deliveries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE integration_deliveries FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON integration_deliveries
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE inbox_messages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE inbox_messages FORCE ROW LEVEL SECURITY;
                CREATE POLICY inbox_messages_read ON inbox_messages FOR SELECT
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                CREATE POLICY inbox_messages_append ON inbox_messages FOR INSERT
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                DROP POLICY tenant_isolation ON outbox_messages;
                CREATE POLICY outbox_messages_read ON outbox_messages FOR SELECT
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                CREATE POLICY outbox_messages_append ON outbox_messages FOR INSERT
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                CREATE TRIGGER outbox_messages_append_only
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON outbox_messages
                    FOR EACH STATEMENT EXECUTE FUNCTION unitatlas_reject_immutable_mutation();
                CREATE TRIGGER inbox_messages_append_only
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON inbox_messages
                    FOR EACH STATEMENT EXECUTE FUNCTION unitatlas_reject_immutable_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS inbox_messages_append_only ON inbox_messages;
                DROP TRIGGER IF EXISTS outbox_messages_append_only ON outbox_messages;
                DROP POLICY outbox_messages_append ON outbox_messages;
                DROP POLICY outbox_messages_read ON outbox_messages;
                CREATE POLICY tenant_isolation ON outbox_messages
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "integration_deliveries");

            migrationBuilder.DropTable(
                name: "integration_endpoints");

            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_TenantId_CreatedAt",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "SubjectType",
                table: "outbox_messages");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessedAt",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_TenantId_ProcessedAt_CreatedAt",
                table: "outbox_messages",
                columns: new[] { "TenantId", "ProcessedAt", "CreatedAt" });
        }
    }
}
