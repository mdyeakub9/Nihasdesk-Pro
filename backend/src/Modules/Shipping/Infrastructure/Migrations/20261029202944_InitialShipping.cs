using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Shipping.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    internal partial class InitialShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shipping");

            migrationBuilder.CreateTable(
                name: "zones",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    areas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    free_over = table.Column<long>(type: "bigint", nullable: true),
                    min_days = table.Column<int>(type: "integer", nullable: true),
                    max_days = table.Column<int>(type: "integer", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zones", x => x.id);
                    table.CheckConstraint("ck_zones_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_zones_free_over", "free_over IS NULL OR free_over >= 0");
                    table.CheckConstraint("ck_zones_price", "price >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_zones_tenant_id_position",
                schema: "shipping",
                table: "zones",
                columns: new[] { "tenant_id", "position" });

            // SEC-11: tenant isolation in the database too (docs/standards/database.md).
            migrationBuilder.EnableTenantRowLevelSecurity("shipping", "zones");
            migrationBuilder.GrantAppRoleAccess("shipping");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "zones",
                schema: "shipping");
        }
    }
}
