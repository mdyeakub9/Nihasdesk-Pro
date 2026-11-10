using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Commerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (ADR 0031 §7, Issue #86): a new table and a nullable column, nothing changed or dropped.
    internal partial class AddProductImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "image_id",
                schema: "commerce",
                table: "product_variants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_images",
                schema: "commerce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    source_url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    source_line = table.Column<int>(type: "integer", nullable: true),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    card_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    thumbnail_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ai_image_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_images", x => x.id);
                    table.CheckConstraint("ck_product_images_position", "position BETWEEN 1 AND 8");
                    table.ForeignKey(
                        name: "fk_product_images_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "commerce",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_image_id",
                schema: "commerce",
                table: "product_variants",
                column: "image_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_images_next_attempt_at",
                schema: "commerce",
                table: "product_images",
                column: "next_attempt_at",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_product_images_product_id_position",
                schema: "commerce",
                table: "product_images",
                columns: new[] { "product_id", "position" });

            migrationBuilder.AddForeignKey(
                name: "fk_product_variants_product_images_image_id",
                schema: "commerce",
                table: "product_variants",
                column: "image_id",
                principalSchema: "commerce",
                principalTable: "product_images",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.EnableTenantRowLevelSecurity("commerce", "product_images");

            // The CSV image download job (Issue #86 PR 3) finds due images of all workspaces: IDs only, no links.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION commerce.due_product_images(p_now timestamptz, p_limit integer)
                RETURNS TABLE (tenant_id uuid, image_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = commerce, pg_temp
                AS $$
                    SELECT i.tenant_id, i.id FROM commerce.product_images i
                    WHERE i.status = 'pending' AND i.next_attempt_at <= p_now
                    ORDER BY i.next_attempt_at
                    LIMIT LEAST(GREATEST(p_limit, 0), 100)
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION commerce.due_product_images(timestamptz, integer) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION commerce.due_product_images(timestamptz, integer) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION commerce.due_product_images(timestamptz, integer);");

            migrationBuilder.DropForeignKey(
                name: "fk_product_variants_product_images_image_id",
                schema: "commerce",
                table: "product_variants");

            migrationBuilder.DropTable(
                name: "product_images",
                schema: "commerce");

            migrationBuilder.DropIndex(
                name: "ix_product_variants_image_id",
                schema: "commerce",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "image_id",
                schema: "commerce",
                table: "product_variants");
        }
    }
}
