using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnitAtlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparatePublicPassportLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public_passport_configs NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public_passport_configs DISABLE ROW LEVEL SECURITY;
                ALTER TABLE units NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE units DISABLE ROW LEVEL SECURITY;

                INSERT INTO public_passport_configs ("UnitId", "TenantId", "PublicId", "IsPublished")
                SELECT "Id", "TenantId",
                    CASE WHEN "AtlasId" = 'UA-KZ-2026-0000058219' THEN 'demo-x200-58219' ELSE gen_random_uuid()::text END,
                    "AtlasId" = 'UA-KZ-2026-0000058219'
                FROM units
                ON CONFLICT ("UnitId") DO NOTHING;

                ALTER TABLE units ENABLE ROW LEVEL SECURITY;
                ALTER TABLE units FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public_passport_configs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public_passport_configs FORCE ROW LEVEL SECURITY;
                """);
        }
    }
}
