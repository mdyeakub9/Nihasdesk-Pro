using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.AiAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (PRD 4.7, ADR 0026 §2): what the reading job needs.
    internal partial class AddSourceReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "page_count",
                schema: "ai",
                table: "knowledge_sources",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "read_attempts",
                schema: "ai",
                table: "knowledge_sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "read_lock_until",
                schema: "ai",
                table: "knowledge_sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "warnings",
                schema: "ai",
                table: "knowledge_sources",
                type: "text[]",
                nullable: false,
                // Sources added before this had nothing to warn about.
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "text_hash",
                schema: "ai",
                table: "knowledge_sections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sections_tenant_id_text_hash",
                schema: "ai",
                table: "knowledge_sections",
                columns: new[] { "tenant_id", "text_hash" });

            // The Workers job finds waiting sources of all workspaces, which RLS hides: IDs only.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION ai.due_sources(p_now timestamptz, p_limit integer)
                RETURNS TABLE (tenant_id uuid, source_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = ai, pg_temp
                AS $$
                    SELECT s.tenant_id, s.id FROM ai.knowledge_sources s
                    WHERE s.status = 'reading' AND (s.read_lock_until IS NULL OR s.read_lock_until <= p_now)
                    ORDER BY s.created_at
                    LIMIT LEAST(GREATEST(p_limit, 0), 100)
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION ai.due_sources(timestamptz, integer) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION ai.due_sources(timestamptz, integer) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION ai.due_sources(timestamptz, integer);");

            migrationBuilder.DropIndex(
                name: "ix_knowledge_sections_tenant_id_text_hash",
                schema: "ai",
                table: "knowledge_sections");

            migrationBuilder.DropColumn(
                name: "page_count",
                schema: "ai",
                table: "knowledge_sources");

            migrationBuilder.DropColumn(
                name: "read_attempts",
                schema: "ai",
                table: "knowledge_sources");

            migrationBuilder.DropColumn(
                name: "read_lock_until",
                schema: "ai",
                table: "knowledge_sources");

            migrationBuilder.DropColumn(
                name: "warnings",
                schema: "ai",
                table: "knowledge_sources");

            migrationBuilder.DropColumn(
                name: "text_hash",
                schema: "ai",
                table: "knowledge_sections");
        }
    }
}
