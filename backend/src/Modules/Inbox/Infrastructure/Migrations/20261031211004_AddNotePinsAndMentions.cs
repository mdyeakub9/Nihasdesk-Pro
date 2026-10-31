using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nihasdesk.Modules.Inbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (PRD 4.4): pinned notes and @mentions on team notes.
    internal partial class AddNotePinsAndMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "mentioned_user_ids",
                schema: "inbox",
                table: "messages",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pinned_at",
                schema: "inbox",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pinned_by_user_id",
                schema: "inbox",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_pinned",
                schema: "inbox",
                table: "messages",
                column: "conversation_id",
                filter: "pinned_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_messages_pinned",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "mentioned_user_ids",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "pinned_at",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "pinned_by_user_id",
                schema: "inbox",
                table: "messages");
        }
    }
}
