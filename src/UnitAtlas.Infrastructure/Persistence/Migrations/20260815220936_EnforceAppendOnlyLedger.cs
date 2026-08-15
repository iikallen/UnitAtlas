using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnitAtlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAppendOnlyLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY tenant_isolation ON trace_events;
                CREATE POLICY trace_events_read ON trace_events FOR SELECT
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                CREATE POLICY trace_events_append ON trace_events FOR INSERT
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                DROP POLICY tenant_isolation ON audit_entries;
                CREATE POLICY audit_entries_read ON audit_entries FOR SELECT
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                CREATE POLICY audit_entries_append ON audit_entries FOR INSERT
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                CREATE FUNCTION unitatlas_reject_immutable_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION '% is append-only', TG_TABLE_NAME USING ERRCODE = '55000';
                END;
                $$;

                CREATE TRIGGER trace_events_append_only
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON trace_events
                    FOR EACH STATEMENT EXECUTE FUNCTION unitatlas_reject_immutable_mutation();
                CREATE TRIGGER audit_entries_append_only
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON audit_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION unitatlas_reject_immutable_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER audit_entries_append_only ON audit_entries;
                DROP TRIGGER trace_events_append_only ON trace_events;
                DROP FUNCTION unitatlas_reject_immutable_mutation();

                DROP POLICY audit_entries_append ON audit_entries;
                DROP POLICY audit_entries_read ON audit_entries;
                CREATE POLICY tenant_isolation ON audit_entries
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);

                DROP POLICY trace_events_append ON trace_events;
                DROP POLICY trace_events_read ON trace_events;
                CREATE POLICY tenant_isolation ON trace_events
                    USING ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);
        }
    }
}
