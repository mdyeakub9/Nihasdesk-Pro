using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Contacts.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (ADR 0032 §6, Issue #91 PR 3): a new table, nothing changed or dropped.
    internal partial class AddProductInterests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_interests",
                schema: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    wanted = table.Column<bool>(type: "boolean", nullable: false),
                    taps = table.Column<int>(type: "integer", nullable: false),
                    first_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_interests", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_interests_contacts_contact_id",
                        column: x => x.contact_id,
                        principalSchema: "contacts",
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_interests_contact_id_last_at",
                schema: "contacts",
                table: "product_interests",
                columns: new[] { "contact_id", "last_at" });

            migrationBuilder.CreateIndex(
                name: "ix_product_interests_tenant_id_contact_id_product_id",
                schema: "contacts",
                table: "product_interests",
                columns: new[] { "tenant_id", "contact_id", "product_id" },
                unique: true);

            migrationBuilder.EnableTenantRowLevelSecurity("contacts", "product_interests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_interests",
                schema: "contacts");
        }
    }
}
