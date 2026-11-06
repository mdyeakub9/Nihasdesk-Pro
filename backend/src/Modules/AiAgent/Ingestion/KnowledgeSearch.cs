using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.SharedKernel;
using Npgsql;
using NpgsqlTypes;

namespace Nihasdesk.Modules.AiAgent.Ingestion;

/// <summary>A part of a source that answers a question. <see cref="Similarity"/>: by meaning, 0–1; null without an embedding.</summary>
internal sealed record Passage(Guid SectionId, Guid SourceId, string SourceTitle, string? HeadingPath, int? Page, string Text, int TokenCount, double? Similarity, double Score);

/// <summary>What is searched: the AI gets <see cref="Live"/>; the owner's test box may also see sources being reviewed.</summary>
internal enum SearchScope
{
    Live,
    LiveAndReview,
}

/// <summary>
/// Finds the best sections for a question (ADR 0026 §3): by meaning (pgvector) and by words
/// (full-text in the section's language), merged with Reciprocal Rank Fusion. In the current
/// workspace only, by the tenant in the query and by RLS; excluded sections never.
/// </summary>
internal sealed class KnowledgeSearch(AiDbContext db, IEmbeddingProvider embeddings, ITenantContext tenant, IOptions<AiOptions> options)
{
    public const int MaxPassages = 4;
    public const int MaxTokens = 1500;
    private const int Candidates = 20;

    // Identifiers and structure only: every value is a parameter (SEC-01).
    private const string Sql = """
        WITH q AS (
            SELECT CAST(@embedding AS vector) AS embedding, unaccent(@query) AS text
        ),
        searchable AS (
            SELECT x.id, x.source_id, s.title, x.heading_path, x.page, x.text, x.token_count, x.embedding, x.search_vector,
                   CASE x.language WHEN 'it' THEN 'italian'::regconfig WHEN 'en' THEN 'english'::regconfig ELSE 'simple'::regconfig END AS config
            FROM ai.knowledge_sections x
            JOIN ai.knowledge_sources s ON s.id = x.source_id
            WHERE x.tenant_id = @tenant AND s.tenant_id = @tenant AND NOT x.excluded
              AND s.status = ANY(@statuses) AND (@source IS NULL OR x.source_id = @source)
        ),
        meaning AS (
            SELECT c.id, 1 - (c.embedding <=> q.embedding) AS similarity,
                   row_number() OVER (ORDER BY c.embedding <=> q.embedding) AS rank
            FROM searchable c, q
            WHERE c.embedding IS NOT NULL AND q.embedding IS NOT NULL
            ORDER BY c.embedding <=> q.embedding
            LIMIT @candidates
        ),
        words AS (
            SELECT c.id, c.search_vector @@ plainto_tsquery(c.config, q.text) AS all_words,
                   row_number() OVER (ORDER BY ts_rank(c.search_vector, replace(plainto_tsquery(c.config, q.text)::text, '&', '|')::tsquery) DESC) AS rank
            FROM searchable c, q
            WHERE c.search_vector @@ replace(plainto_tsquery(c.config, q.text)::text, '&', '|')::tsquery
            ORDER BY rank
            LIMIT @candidates
        )
        SELECT c.id AS section_id, c.source_id, c.title AS source_title, c.heading_path, c.page, c.text, c.token_count,
               m.similarity::float8 AS similarity,
               COALESCE(w.all_words, false) AS all_words,
               (COALESCE(1.0 / (60 + m.rank), 0) + COALESCE(1.0 / (60 + w.rank), 0))::float8 AS score
        FROM searchable c
        LEFT JOIN meaning m ON m.id = c.id
        LEFT JOIN words w ON w.id = c.id
        WHERE m.id IS NOT NULL OR w.id IS NOT NULL
        ORDER BY score DESC, c.id
        LIMIT @candidates
        """;

    /// <summary>
    /// The best sections, at most 4 and about 1,500 tokens; empty when nothing matches well enough,
    /// so the AI hands over instead of guessing (PRD 4.7). Good enough: close in meaning, or has
    /// every word of the question. With <paramref name="sourceId"/>, only that source.
    /// </summary>
    public async Task<List<Passage>> SearchAsync(string query, SearchScope scope, Guid? sourceId, CancellationToken ct)
    {
        var statuses = scope == SearchScope.Live
            ? new[] { "live" }
            : ["live", "review"];
        // No sources: no search and no call to the provider, which costs money for every customer message.
        if (!await db.Sources.AnyAsync(s => s.Status == SourceStatus.Live || (scope == SearchScope.LiveAndReview && s.Status == SourceStatus.Review), ct))
        {
            return [];
        }

        var rows = await db.Database.SqlQueryRaw<Row>(
                Sql,
                new NpgsqlParameter("embedding", NpgsqlDbType.Text) { Value = (object?)await EmbedAsync(query, ct) ?? DBNull.Value },
                new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = query },
                new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenant.TenantId },
                new NpgsqlParameter("statuses", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = statuses },
                new NpgsqlParameter("source", NpgsqlDbType.Uuid) { Value = (object?)sourceId ?? DBNull.Value },
                new NpgsqlParameter("candidates", NpgsqlDbType.Integer) { Value = Candidates })
            .ToListAsync(ct);

        var passages = new List<Passage>();
        var tokens = 0;
        foreach (var row in rows.Where(r => r.AllWords || r.Similarity >= options.Value.KnowledgeMinSimilarity))
        {
            if (passages.Count == MaxPassages || (passages.Count > 0 && tokens + row.TokenCount > MaxTokens))
            {
                break;
            }

            passages.Add(new Passage(row.SectionId, row.SourceId, row.SourceTitle, row.HeadingPath, row.Page, row.Text, row.TokenCount, row.Similarity, row.Score));
            tokens += row.TokenCount;
        }

        return passages;
    }

    /// <summary>Without a key, or when the provider fails, words alone still find sections.</summary>
    private async Task<string?> EmbedAsync(string query, CancellationToken ct)
    {
        if (!embeddings.IsConfigured)
        {
            return null;
        }

        try
        {
            return SectionIndex.VectorText((await embeddings.EmbedAsync([query], ct))[0]);
        }
        catch (LlmException)
        {
            return null;
        }
    }

    private sealed record Row(Guid SectionId, Guid SourceId, string SourceTitle, string? HeadingPath, int? Page, string Text, int TokenCount, double? Similarity, bool AllWords, double Score);
}
