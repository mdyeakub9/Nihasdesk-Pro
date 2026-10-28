using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Commerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    internal partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "commerce");

            migrationBuilder.CreateTable(
                name: "products",
                schema: "commerce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    handle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    product_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    category = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    vendor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    option_names = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_variants",
                schema: "commerce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    option1 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    option2 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    option3 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    compare_at_price = table.Column<long>(type: "bigint", nullable: true),
                    cost = table.Column<long>(type: "bigint", nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    track_inventory = table.Column<bool>(type: "boolean", nullable: false),
                    stock = table.Column<int>(type: "integer", nullable: true),
                    inventory_policy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    weight_grams = table.Column<int>(type: "integer", nullable: true),
                    requires_shipping = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variants", x => x.id);
                    table.CheckConstraint("ck_product_variants_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_product_variants_price", "price >= 0");
                    table.ForeignKey(
                        name: "fk_product_variants_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "commerce",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_product_id",
                schema: "commerce",
                table: "product_variants",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_tenant_id_sku",
                schema: "commerce",
                table: "product_variants",
                columns: new[] { "tenant_id", "sku" },
                unique: true,
                filter: "sku IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_created_at_id",
                schema: "commerce",
                table: "products",
                columns: new[] { "tenant_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_handle",
                schema: "commerce",
                table: "products",
                columns: new[] { "tenant_id", "handle" },
                unique: true);

            // SEC-11: tenant isolation in the database too (docs/standards/database.md).
            migrationBuilder.EnableTenantRowLevelSecurity("commerce", "products");
            migrationBuilder.EnableTenantRowLevelSecurity("commerce", "product_variants");
            migrationBuilder.GrantAppRoleAccess("commerce");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_variants",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "products",
                schema: "commerce");
        }
    }
}
