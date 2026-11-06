using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nihasdesk.Modules.AiAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Internal: only the module entry point may be public (architecture tests).
    // Additive only (PRD 4.7, ADR 0026 §3): indexes for hybrid search. No model change.
    internal partial class AddKnowledgeSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The migration user must be allowed to create it, like vector.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");

            // Customers often leave accents out: sections read before this are indexed again without them.
            migrationBuilder.Sql(
                """
                UPDATE ai.knowledge_sections
                SET search_vector = to_tsvector(
                    CASE language WHEN 'it' THEN 'italian'::regconfig WHEN 'en' THEN 'english'::regconfig ELSE 'simple'::regconfig END,
                    unaccent(coalesce(heading_path, '') || ' ' || text));
                """);

            migrationBuilder.Sql("CREATE INDEX ix_knowledge_sections_embedding ON ai.knowledge_sections USING hnsw (embedding vector_cosine_ops);");
            migrationBuilder.Sql("CREATE INDEX ix_knowledge_sections_search_vector ON ai.knowledge_sections USING gin (search_vector);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX ai.ix_knowledge_sections_search_vector;");
            migrationBuilder.Sql("DROP INDEX ai.ix_knowledge_sections_embedding;");
        }
    }
}
