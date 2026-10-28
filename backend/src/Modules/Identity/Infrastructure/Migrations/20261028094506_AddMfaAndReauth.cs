using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nihasdesk.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    internal partial class AddMfaAndReauth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reauthenticated_at",
                schema: "identity",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recovery_codes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recovery_codes_user_id",
                schema: "identity",
                table: "recovery_codes",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recovery_codes",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "reauthenticated_at",
                schema: "identity",
                table: "sessions");
        }
    }
}
