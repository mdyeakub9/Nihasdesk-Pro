using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Inbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests). Additive only (ADR 0031 §1).
    internal partial class AddMessageAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_attachments",
                schema: "inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    source_url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    thumbnail_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ai_image_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    file_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_attachments_messages_message_id",
                        column: x => x.message_id,
                        principalSchema: "inbox",
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_attachments_message_id_position",
                schema: "inbox",
                table: "message_attachments",
                columns: new[] { "message_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_message_attachments_next_attempt_at",
                schema: "inbox",
                table: "message_attachments",
                column: "next_attempt_at",
                filter: "status = 'pending'");

            migrationBuilder.EnableTenantRowLevelSecurity("inbox", "message_attachments");

            // The download job finds due media of all workspaces: IDs only, no links (ADR 0031 §2).
            migrationBuilder.Sql(
                """
                CREATE FUNCTION inbox.due_media(p_now timestamptz, p_limit integer)
                RETURNS TABLE (tenant_id uuid, attachment_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = inbox, pg_temp
                AS $$
                    SELECT a.tenant_id, a.id FROM inbox.message_attachments a
                    WHERE a.status = 'pending' AND a.next_attempt_at <= p_now
                    ORDER BY a.next_attempt_at
                    LIMIT LEAST(GREATEST(p_limit, 0), 100)
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION inbox.due_media(timestamptz, integer) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION inbox.due_media(timestamptz, integer) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION inbox.due_media(timestamptz, integer);");

            migrationBuilder.DropTable(
                name: "message_attachments",
                schema: "inbox");
        }
    }
}
