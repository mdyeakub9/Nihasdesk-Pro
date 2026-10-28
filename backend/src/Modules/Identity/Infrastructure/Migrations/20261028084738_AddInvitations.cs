using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    internal partial class AddInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitations",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_invitations_workspaces_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "identity",
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_tenant_id_email",
                schema: "identity",
                table: "invitations",
                columns: new[] { "tenant_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_token_hash",
                schema: "identity",
                table: "invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.EnableTenantRowLevelSecurity("identity", "invitations");

            // The invite link carries only the token, so the tenant is unknown until the token is
            // found. This narrow function (owner rights) maps a token hash to its invitation and
            // tenant and nothing else (ADR 0014); the app role may execute it only.
            migrationBuilder.Sql("""
                CREATE FUNCTION identity.find_invitation(p_token_hash bytea)
                RETURNS TABLE (id uuid, tenant_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = identity, pg_temp
                AS $$
                    SELECT i.id, i.tenant_id FROM identity.invitations i WHERE i.token_hash = p_token_hash
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION identity.find_invitation(bytea) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION identity.find_invitation(bytea) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION identity.find_invitation(bytea);");

            migrationBuilder.DropTable(
                name: "invitations",
                schema: "identity");
        }
    }
}
