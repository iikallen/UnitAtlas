using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnitAtlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityAndTenantIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSubject = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_memberships_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_memberships_TenantId_UserSubject",
                table: "tenant_memberships",
                columns: new[] { "TenantId", "UserSubject" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE products ENABLE ROW LEVEL SECURITY;
                ALTER TABLE products FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON products
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE units ENABLE ROW LEVEL SECURITY;
                ALTER TABLE units FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON units
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE trace_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE trace_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON trace_events
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE unit_states ENABLE ROW LEVEL SECURITY;
                ALTER TABLE unit_states FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON unit_states
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE tenant_memberships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenant_memberships FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON tenant_memberships
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY tenant_isolation ON products;
                ALTER TABLE products NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE products DISABLE ROW LEVEL SECURITY;
                DROP POLICY tenant_isolation ON units;
                ALTER TABLE units NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE units DISABLE ROW LEVEL SECURITY;
                DROP POLICY tenant_isolation ON trace_events;
                ALTER TABLE trace_events NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE trace_events DISABLE ROW LEVEL SECURITY;
                DROP POLICY tenant_isolation ON unit_states;
                ALTER TABLE unit_states NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE unit_states DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "tenant_memberships");
        }
    }
}
