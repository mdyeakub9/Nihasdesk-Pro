using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nihasdesk.Modules.Inbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (ADR 0032 §6, Issue #91 PR 3): three nullable columns, nothing changed or dropped.
    internal partial class AddLeadReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "lead_reason",
                schema: "inbox",
                table: "conversations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lead_reason_at",
                schema: "inbox",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lead_reason_product_id",
                schema: "inbox",
                table: "conversations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lead_reason",
                schema: "inbox",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "lead_reason_at",
                schema: "inbox",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "lead_reason_product_id",
                schema: "inbox",
                table: "conversations");
        }
    }
}
