using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.AiAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests). Additive only (ADR 0032 §4, owner decision B).
    internal partial class AddOrderDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "ai",
                table: "runs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Every existing run was answered by the model.
                defaultValue: "model");

            migrationBuilder.CreateTable(
                name: "order_drafts",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    preselected_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chosen_values = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
                    last_query = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shown_product_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    summary = table.Column<string>(type: "jsonb", nullable: true),
                    summary_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_drafts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_drafts_tenant_id_contact_id",
                schema: "ai",
                table: "order_drafts",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_order_drafts_tenant_id_conversation_id",
                schema: "ai",
                table: "order_drafts",
                columns: new[] { "tenant_id", "conversation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_drafts_updated_at",
                schema: "ai",
                table: "order_drafts",
                column: "updated_at");

            migrationBuilder.EnableTenantRowLevelSecurity("ai", "order_drafts");

            // The retention job removes old drafts (with any delivery details) of all workspaces (SEC-44).
            migrationBuilder.Sql(
                """
                CREATE FUNCTION ai.purge_old_drafts(p_before timestamptz)
                RETURNS integer
                LANGUAGE sql VOLATILE SECURITY DEFINER
                SET search_path = ai, pg_temp
                AS $$
                    WITH purged AS (
                        DELETE FROM ai.order_drafts WHERE updated_at < p_before
                        RETURNING 1
                    )
                    SELECT count(*)::integer FROM purged;
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION ai.purge_old_drafts(timestamptz) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION ai.purge_old_drafts(timestamptz) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION ai.purge_old_drafts(timestamptz);");

            migrationBuilder.DropTable(
                name: "order_drafts",
                schema: "ai");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "ai",
                table: "runs");
        }
    }
}
