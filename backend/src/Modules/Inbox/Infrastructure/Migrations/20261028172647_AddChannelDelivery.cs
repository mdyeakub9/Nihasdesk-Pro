using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Inbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    internal partial class AddChannelDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "delivery",
                schema: "inbox",
                table: "messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Existing messages have nothing to deliver.
                defaultValue: "none");

            migrationBuilder.AddColumn<int>(
                name: "delivery_attempts",
                schema: "inbox",
                table: "messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "delivery_error",
                schema: "inbox",
                table: "messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_delivery_at",
                schema: "inbox",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "channel_connection_id",
                schema: "inbox",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_next_delivery_at",
                schema: "inbox",
                table: "messages",
                column: "next_delivery_at",
                filter: "delivery = 'pending'");

            // The background sender finds due replies of all workspaces: IDs only, no content (ADR 0017).
            migrationBuilder.Sql(
                """
                CREATE FUNCTION inbox.due_deliveries(p_now timestamptz, p_limit integer)
                RETURNS TABLE (tenant_id uuid, message_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = inbox, pg_temp
                AS $$
                    SELECT m.tenant_id, m.id FROM inbox.messages m
                    WHERE m.delivery = 'pending' AND m.next_delivery_at <= p_now
                    ORDER BY m.next_delivery_at
                    LIMIT LEAST(GREATEST(p_limit, 0), 100)
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION inbox.due_deliveries(timestamptz, integer) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION inbox.due_deliveries(timestamptz, integer) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION inbox.due_deliveries(timestamptz, integer);");

            migrationBuilder.DropIndex(
                name: "ix_messages_next_delivery_at",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "delivery",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "delivery_attempts",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "delivery_error",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "next_delivery_at",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "channel_connection_id",
                schema: "inbox",
                table: "conversations");
        }
    }
}
