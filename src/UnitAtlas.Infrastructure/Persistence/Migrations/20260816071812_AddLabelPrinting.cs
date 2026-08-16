using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnitAtlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelPrinting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "label_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    IdentifierMode = table.Column<string>(type: "text", nullable: false),
                    Symbology = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_templates", x => x.Id);
                    table.UniqueConstraint("AK_label_templates_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_label_templates_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "print_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    IdentifierMode = table.Column<string>(type: "text", nullable: false),
                    Gs1CompanyPrefix = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_print_profiles", x => x.Id);
                    table.UniqueConstraint("AK_print_profiles_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_print_profiles_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "printers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Transport = table.Column<string>(type: "text", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_printers", x => x.Id);
                    table.UniqueConstraint("AK_printers_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_printers_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "print_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    RequestHash = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PrintedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_print_jobs", x => x.Id);
                    table.UniqueConstraint("AK_print_jobs_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_print_jobs_label_templates_TenantId_TemplateId",
                        columns: x => new { x.TenantId, x.TemplateId },
                        principalTable: "label_templates",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_print_jobs_print_profiles_TenantId_ProfileId",
                        columns: x => new { x.TenantId, x.ProfileId },
                        principalTable: "print_profiles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_print_jobs_printers_TenantId_PrinterId",
                        columns: x => new { x.TenantId, x.PrinterId },
                        principalTable: "printers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_print_jobs_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "print_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrintJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_print_attempts", x => x.Id);
                    table.UniqueConstraint("AK_print_attempts_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_print_attempts_print_jobs_TenantId_PrintJobId",
                        columns: x => new { x.TenantId, x.PrintJobId },
                        principalTable: "print_jobs",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_print_attempts_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "print_job_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrintJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    HumanReadable = table.Column<string>(type: "text", nullable: false),
                    Copies = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_print_job_items", x => x.Id);
                    table.UniqueConstraint("AK_print_job_items_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_print_job_items_print_jobs_TenantId_PrintJobId",
                        columns: x => new { x.TenantId, x.PrintJobId },
                        principalTable: "print_jobs",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_print_job_items_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_label_templates_TenantId_Code",
                table: "label_templates",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_print_attempts_TenantId_PrintJobId",
                table: "print_attempts",
                columns: new[] { "TenantId", "PrintJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_print_job_items_TenantId_PrintJobId",
                table: "print_job_items",
                columns: new[] { "TenantId", "PrintJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_print_jobs_TenantId_IdempotencyKey",
                table: "print_jobs",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_print_jobs_TenantId_PrinterId",
                table: "print_jobs",
                columns: new[] { "TenantId", "PrinterId" });

            migrationBuilder.CreateIndex(
                name: "IX_print_jobs_TenantId_ProfileId",
                table: "print_jobs",
                columns: new[] { "TenantId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_print_jobs_TenantId_TemplateId",
                table: "print_jobs",
                columns: new[] { "TenantId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_print_profiles_TenantId_Code",
                table: "print_profiles",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_printers_TenantId_Code",
                table: "printers",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE label_templates ENABLE ROW LEVEL SECURITY;
                ALTER TABLE label_templates FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON label_templates
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE print_profiles ENABLE ROW LEVEL SECURITY;
                ALTER TABLE print_profiles FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON print_profiles
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE printers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE printers FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON printers
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE print_jobs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE print_jobs FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON print_jobs
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE print_job_items ENABLE ROW LEVEL SECURITY;
                ALTER TABLE print_job_items FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON print_job_items
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                ALTER TABLE print_attempts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE print_attempts FORCE ROW LEVEL SECURITY;
                CREATE POLICY print_attempts_read ON print_attempts FOR SELECT
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                CREATE POLICY print_attempts_append ON print_attempts FOR INSERT
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                CREATE TRIGGER print_attempts_append_only
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON print_attempts
                    FOR EACH STATEMENT EXECUTE FUNCTION unitatlas_reject_immutable_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS print_attempts_append_only ON print_attempts;");

            migrationBuilder.DropTable(
                name: "print_attempts");

            migrationBuilder.DropTable(
                name: "print_job_items");

            migrationBuilder.DropTable(
                name: "print_jobs");

            migrationBuilder.DropTable(
                name: "label_templates");

            migrationBuilder.DropTable(
                name: "print_profiles");

            migrationBuilder.DropTable(
                name: "printers");
        }
    }
}
