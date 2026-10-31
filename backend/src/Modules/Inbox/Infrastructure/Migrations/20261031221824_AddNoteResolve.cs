using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nihasdesk.Modules.Inbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (PRD 4.4): resolved team notes.
    internal partial class AddNoteResolve : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolved_at",
                schema: "inbox",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_by_user_id",
                schema: "inbox",
                table: "messages",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "resolved_at",
                schema: "inbox",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "resolved_by_user_id",
                schema: "inbox",
                table: "messages");
        }
    }
}
