using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nihasdesk.Infrastructure.Persistence;

#nullable disable

namespace Nihasdesk.Modules.AiAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (PRD 4.7, ADR 0026 §1): sources and their sections, tenant-owned with RLS.
    internal partial class AddKnowledgeSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The migration user must be allowed to create it (the database owner is, locally and in the image).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.CreateTable(
                name: "knowledge_sources",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    origin = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    last_read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_sections",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    heading_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    page = table.Column<int>(type: "integer", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    excluded = table.Column<bool>(type: "boolean", nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    embedding_model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_sections", x => x.id);
                    table.ForeignKey(
                        name: "fk_knowledge_sections_knowledge_sources_source_id",
                        column: x => x.source_id,
                        principalSchema: "ai",
                        principalTable: "knowledge_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sections_source_id",
                schema: "ai",
                table: "knowledge_sections",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sections_tenant_id_source_id_position",
                schema: "ai",
                table: "knowledge_sections",
                columns: new[] { "tenant_id", "source_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sources_tenant_id_content_hash",
                schema: "ai",
                table: "knowledge_sources",
                columns: new[] { "tenant_id", "content_hash" },
                unique: true,
                filter: "content_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sources_tenant_id_created_at",
                schema: "ai",
                table: "knowledge_sources",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sources_tenant_id_origin",
                schema: "ai",
                table: "knowledge_sources",
                columns: new[] { "tenant_id", "origin" },
                unique: true,
                filter: "kind = 'webpage'");

            // Not mapped in EF (no new package for it): only search SQL reads and writes these.
            // Their search indexes come with hybrid search (Issue #53).
            migrationBuilder.Sql("ALTER TABLE ai.knowledge_sections ADD COLUMN embedding vector(1536), ADD COLUMN search_vector tsvector;");

            // SEC-11; the schema grant from InitialAi covers new tables.
            migrationBuilder.EnableTenantRowLevelSecurity("ai", "knowledge_sources");
            migrationBuilder.EnableTenantRowLevelSecurity("ai", "knowledge_sections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_sections",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "knowledge_sources",
                schema: "ai");
        }
    }
}
