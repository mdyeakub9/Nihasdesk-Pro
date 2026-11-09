using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Inbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests). Additive only (ADR 0032 §1, §3).
    internal partial class AddRichMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "action_id",
                schema: "inbox",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "fallback_text",
                schema: "inbox",
                table: "messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "inbox",
                table: "messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Every existing message is a plain message.
                defaultValue: "text");

            migrationBuilder.AddColumn<string>(
                name: "rich_content",
                schema: "inbox",
                table: "messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rich_kind",
                schema: "inbox",
                table: "messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rich_rendered",
                schema: "inbox",
                table: "messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "message_actions",
                schema: "inbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character(22)", fixedLength: true, maxLength: 22, nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_actions_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "inbox",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_message_actions_messages_message_id",
                        column: x => x.message_id,
                        principalSchema: "inbox",
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_messages_action_id",
                schema: "inbox",
                table: "messages",
                column: "action_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_actions_conversation_id",
                schema: "inbox",
                table: "message_actions",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_actions_message_id_position",
                schema: "inbox",
                table: "message_actions",
                columns: new[] { "message_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_message_actions_tenant_id_token",
                schema: "inbox",
                table: "message_actions",
                columns: new[] { "tenant_id", "token" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_messages_message_actions_action_id",
                schema: "inbox",
                table: "messages",
                column: "action_id",
                principalSchema: "inbox",
                principalTable: "message_actions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.EnableTenantRowLevelSecurity("inbox", "message_actions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_messages_message_actions_action_id",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropTable(
                name: "message_actions",
                schema: "inbox");

            migrationBuilder.DropIndex(
                name: "ix_messages_action_id",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "action_id",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "fallback_text",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "rich_content",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "rich_kind",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "rich_rendered",
                schema: "inbox",
                table: "messages");
        }
    }
}
