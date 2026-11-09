using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.Inbox.Infrastructure.Migrations
{
    /// <summary>
    /// Media retention (SEC-44, ADR 0031 §8). Additive: one lookup function, no table change. The new
    /// attachment status <c>deleted</c> fits the existing text column.
    /// </summary>
    internal partial class AddMediaRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Saved media whose message is older than p_before, across workspaces for the daily job.
            // SECURITY DEFINER like due_media: it returns IDs only, never keys or content.
            migrationBuilder.Sql("""
                CREATE FUNCTION inbox.expired_media(p_before timestamptz, p_limit integer)
                RETURNS TABLE (tenant_id uuid, attachment_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = inbox, pg_temp
                AS $$
                    SELECT a.tenant_id, a.id FROM inbox.message_attachments a
                    JOIN inbox.messages m ON m.id = a.message_id
                    WHERE a.status = 'saved' AND m.sent_at < p_before
                    ORDER BY m.sent_at
                    LIMIT LEAST(GREATEST(p_limit, 0), 100)
                $$;
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION inbox.expired_media(timestamptz, integer) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION inbox.expired_media(timestamptz, integer) TO {RlsMigrationExtensions.AppRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION inbox.expired_media(timestamptz, integer);");
        }
    }
}
