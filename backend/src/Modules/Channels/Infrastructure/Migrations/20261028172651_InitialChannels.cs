using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Channels.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    internal partial class InitialChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "channels");

            migrationBuilder.CreateTable(
                name: "channel_connections",
                schema: "channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_account_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    protected_token = table.Column<string>(type: "text", nullable: false),
                    connected_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disconnected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_events",
                schema: "channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    protected_payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_connections_tenant_id_kind_external_account_id",
                schema: "channels",
                table: "channel_connections",
                columns: new[] { "tenant_id", "kind", "external_account_id" });

            migrationBuilder.CreateIndex(
                name: "ux_channel_connections_active_account",
                schema: "channels",
                table: "channel_connections",
                columns: new[] { "kind", "external_account_id" },
                unique: true,
                filter: "disconnected_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_next_attempt_at",
                schema: "channels",
                table: "webhook_events",
                column: "next_attempt_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_processed_at",
                schema: "channels",
                table: "webhook_events",
                column: "processed_at");

            // SEC-11 for connections. webhook_events is not tenant-owned (ADR 0017): payloads are encrypted.
            migrationBuilder.EnableTenantRowLevelSecurity("channels", "channel_connections");
            migrationBuilder.GrantAppRoleAccess("channels");

            // Webhooks name a Page, not a workspace: the narrow lookup from Page to workspace (ADR 0017).
            migrationBuilder.Sql(
                """
                CREATE FUNCTION channels.find_connection(p_kind text, p_external_id text)
                RETURNS TABLE (id uuid, tenant_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = channels, pg_temp
                AS $$
                    SELECT c.id, c.tenant_id FROM channels.channel_connections c
                    WHERE c.kind = p_kind AND c.external_account_id = p_external_id AND c.disconnected_at IS NULL
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION channels.find_connection(text, text) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION channels.find_connection(text, text) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION channels.find_connection(text, text);");

            migrationBuilder.DropTable(
                name: "channel_connections",
                schema: "channels");

            migrationBuilder.DropTable(
                name: "webhook_events",
                schema: "channels");
        }
    }
}
