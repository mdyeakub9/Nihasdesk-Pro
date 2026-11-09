using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.AiAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (Issue #80, ADR 0030): websites the owner gives by domain.
    internal partial class AddKnowledgeSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "site_id",
                schema: "ai",
                table: "knowledge_sources",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "knowledge_sites",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    root_url = table.Column<string>(type: "character varying(265)", maxLength: 265, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    pages = table.Column<string>(type: "jsonb", nullable: false),
                    find_attempts = table.Column<int>(type: "integer", nullable: false),
                    find_lock_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_sites", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sources_site_id",
                schema: "ai",
                table: "knowledge_sources",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sites_status",
                schema: "ai",
                table: "knowledge_sites",
                column: "status",
                filter: "status = 'finding'");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sites_tenant_id_host",
                schema: "ai",
                table: "knowledge_sites",
                columns: new[] { "tenant_id", "host" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_knowledge_sources_knowledge_sites_site_id",
                schema: "ai",
                table: "knowledge_sources",
                column: "site_id",
                principalSchema: "ai",
                principalTable: "knowledge_sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.EnableTenantRowLevelSecurity("ai", "knowledge_sites");

            // The Workers job finds waiting sites of all workspaces, which RLS hides: IDs only.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION ai.due_sites(p_now timestamptz, p_limit integer)
                RETURNS TABLE (tenant_id uuid, site_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = ai, pg_temp
                AS $$
                    SELECT s.tenant_id, s.id FROM ai.knowledge_sites s
                    WHERE s.status = 'finding' AND (s.find_lock_until IS NULL OR s.find_lock_until <= p_now)
                    ORDER BY s.created_at
                    LIMIT LEAST(GREATEST(p_limit, 0), 100)
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION ai.due_sites(timestamptz, integer) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION ai.due_sites(timestamptz, integer) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION ai.due_sites(timestamptz, integer);");

            migrationBuilder.DropForeignKey(
                name: "fk_knowledge_sources_knowledge_sites_site_id",
                schema: "ai",
                table: "knowledge_sources");

            migrationBuilder.DropTable(
                name: "knowledge_sites",
                schema: "ai");

            migrationBuilder.DropIndex(
                name: "ix_knowledge_sources_site_id",
                schema: "ai",
                table: "knowledge_sources");

            migrationBuilder.DropColumn(
                name: "site_id",
                schema: "ai",
                table: "knowledge_sources");
        }
    }
}
