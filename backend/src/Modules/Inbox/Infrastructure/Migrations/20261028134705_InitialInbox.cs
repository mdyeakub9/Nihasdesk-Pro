using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Inbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    internal partial class InitialInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inbox");

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    responder = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ai_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lead_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_inbound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    preview = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    @internal = table.Column<bool>(name: "internal", type: "boolean", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "inbox",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "read_markers",
                schema: "inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_read_markers", x => x.id);
                    table.ForeignKey(
                        name: "fk_read_markers_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "inbox",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_contact_id_channel",
                schema: "inbox",
                table: "conversations",
                columns: new[] { "tenant_id", "contact_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_last_message_at_id",
                schema: "inbox",
                table: "conversations",
                columns: new[] { "tenant_id", "last_message_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_sent_at_id",
                schema: "inbox",
                table: "messages",
                columns: new[] { "conversation_id", "sent_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_conversation_id_external_id",
                schema: "inbox",
                table: "messages",
                columns: new[] { "tenant_id", "conversation_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_read_markers_conversation_id_user_id",
                schema: "inbox",
                table: "read_markers",
                columns: new[] { "conversation_id", "user_id" },
                unique: true);

            // SEC-11: tenant isolation in the database too (docs/standards/database.md).
            migrationBuilder.EnableTenantRowLevelSecurity("inbox", "conversations");
            migrationBuilder.EnableTenantRowLevelSecurity("inbox", "messages");
            migrationBuilder.EnableTenantRowLevelSecurity("inbox", "read_markers");
            migrationBuilder.GrantAppRoleAccess("inbox");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages",
                schema: "inbox");

            migrationBuilder.DropTable(
                name: "read_markers",
                schema: "inbox");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "inbox");
        }
    }
}
