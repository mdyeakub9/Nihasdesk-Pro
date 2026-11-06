using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.AiAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (ADR 0026 §4): the running summary of a conversation.
    internal partial class AddConversationMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_memory",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    covered_up_to_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    covered_up_to_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    summary_lock_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    summary_attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_memory", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_memory_summary_due_at",
                schema: "ai",
                table: "conversation_memory",
                column: "summary_due_at",
                filter: "summary_due_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_memory_tenant_id_contact_id",
                schema: "ai",
                table: "conversation_memory",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_memory_tenant_id_conversation_id",
                schema: "ai",
                table: "conversation_memory",
                columns: new[] { "tenant_id", "conversation_id" },
                unique: true);

            migrationBuilder.EnableTenantRowLevelSecurity("ai", "conversation_memory");

            // The Workers job finds due summaries of all workspaces, which RLS hides: IDs only.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION ai.due_memory_summaries(p_now timestamptz, p_limit integer)
                RETURNS TABLE (tenant_id uuid, conversation_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = ai, pg_temp
                AS $$
                    SELECT m.tenant_id, m.conversation_id FROM ai.conversation_memory m
                    WHERE m.summary_due_at IS NOT NULL AND m.summary_due_at <= p_now
                      AND (m.summary_lock_until IS NULL OR m.summary_lock_until <= p_now)
                    ORDER BY m.summary_due_at
                    LIMIT LEAST(GREATEST(p_limit, 0), 100)
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION ai.due_memory_summaries(timestamptz, integer) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION ai.due_memory_summaries(timestamptz, integer) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION ai.due_memory_summaries(timestamptz, integer);");

            migrationBuilder.DropTable(
                name: "conversation_memory",
                schema: "ai");
        }
    }
}
