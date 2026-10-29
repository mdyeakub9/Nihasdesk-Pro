using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.AiAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    internal partial class AddShopPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "sources",
                schema: "ai",
                table: "runs",
                type: "text[]",
                nullable: false,
                // Runs before this PR used no knowledge sources.
                defaultValueSql: "'{}'");

            migrationBuilder.CreateTable(
                name: "shop_policies",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipping_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    return_days = table.Column<int>(type: "integer", nullable: true),
                    return_policy = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    payment_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    contact_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    opening_hours = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_policies", x => x.id);
                    table.CheckConstraint("ck_shop_policies_return_days", "return_days IS NULL OR return_days BETWEEN 0 AND 365");
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_policies_tenant_id",
                schema: "ai",
                table: "shop_policies",
                column: "tenant_id",
                unique: true);

            // SEC-11: tenant isolation in the database too (docs/standards/database.md).
            migrationBuilder.EnableTenantRowLevelSecurity("ai", "shop_policies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_policies",
                schema: "ai");

            migrationBuilder.DropColumn(
                name: "sources",
                schema: "ai",
                table: "runs");
        }
    }
}
