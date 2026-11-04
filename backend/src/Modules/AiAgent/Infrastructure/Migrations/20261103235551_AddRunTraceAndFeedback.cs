using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.AiAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (PRD 4.8, ADR 0026 §6): run traces, the message a run sent, and 👍/👎 on AI replies.
    internal partial class AddRunTraceAndFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "instruction_version",
                schema: "ai",
                table: "runs",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                schema: "ai",
                table: "runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trace",
                schema: "ai",
                table: "runs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reply_feedback",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reply_feedback", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_runs_tenant_id_message_id",
                schema: "ai",
                table: "runs",
                columns: new[] { "tenant_id", "message_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reply_feedback_tenant_id_message_id_user_id",
                schema: "ai",
                table: "reply_feedback",
                columns: new[] { "tenant_id", "message_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reply_feedback_tenant_id_rating_created_at",
                schema: "ai",
                table: "reply_feedback",
                columns: new[] { "tenant_id", "rating", "created_at" });

            // SEC-11: feedback is tenant-owned like every ai table; the schema grant covers new tables.
            migrationBuilder.EnableTenantRowLevelSecurity("ai", "reply_feedback");

            // SEC-40 retention for all workspaces, which RLS hides from the app: this narrow function
            // (owner rights) only clears traces older than the given time and returns how many.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION ai.purge_old_traces(p_before timestamptz)
                RETURNS integer
                LANGUAGE sql VOLATILE SECURITY DEFINER
                SET search_path = ai, pg_temp
                AS $$
                    WITH purged AS (
                        UPDATE ai.runs SET trace = NULL
                        WHERE trace IS NOT NULL AND created_at < p_before
                        RETURNING 1
                    )
                    SELECT count(*)::integer FROM purged;
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION ai.purge_old_traces(timestamptz) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION ai.purge_old_traces(timestamptz) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION ai.purge_old_traces(timestamptz);");

            migrationBuilder.DropTable(
                name: "reply_feedback",
                schema: "ai");

            migrationBuilder.DropIndex(
                name: "ix_runs_tenant_id_message_id",
                schema: "ai",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "instruction_version",
                schema: "ai",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "message_id",
                schema: "ai",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "trace",
                schema: "ai",
                table: "runs");
        }
    }
}
