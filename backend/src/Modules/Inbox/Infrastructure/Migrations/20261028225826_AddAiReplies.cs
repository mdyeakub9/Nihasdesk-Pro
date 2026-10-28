using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Inbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    internal partial class AddAiReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ai_reply_due_at",
                schema: "inbox",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_ai_reply_due_at",
                schema: "inbox",
                table: "conversations",
                column: "ai_reply_due_at",
                filter: "ai_reply_due_at IS NOT NULL");

            // The AI reply processor finds due conversations of all workspaces: IDs only (ADR 0019).
            migrationBuilder.Sql(
                """
                CREATE FUNCTION inbox.due_ai_replies(p_now timestamptz, p_limit integer)
                RETURNS TABLE (tenant_id uuid, conversation_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = inbox, pg_temp
                AS $$
                    SELECT c.tenant_id, c.id FROM inbox.conversations c
                    WHERE c.ai_reply_due_at <= p_now
                    ORDER BY c.ai_reply_due_at
                    LIMIT LEAST(GREATEST(p_limit, 0), 100)
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION inbox.due_ai_replies(timestamptz, integer) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION inbox.due_ai_replies(timestamptz, integer) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION inbox.due_ai_replies(timestamptz, integer);");

            migrationBuilder.DropIndex(
                name: "ix_conversations_ai_reply_due_at",
                schema: "inbox",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "ai_reply_due_at",
                schema: "inbox",
                table: "conversations");
        }
    }
}
