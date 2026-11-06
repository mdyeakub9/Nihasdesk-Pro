using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.Modules.AiAgent.Ingestion;

/// <summary>
/// What makes sections findable: the full-text index and the embedding (ADR 0026 §2–3). Used when
/// a source is read and when the owner edits a section.
/// </summary>
internal sealed class SectionIndex(AiDbContext db, IEmbeddingProvider embeddings)
{
    public const int EmbeddingBatch = 64;

    /// <summary>Title and heading path in front, so a passage still makes sense on its own (ADR 0026 §2).</summary>
    public static string EmbeddingText(string title, KnowledgeSection section) =>
        $"{title}{(section.HeadingPath is null ? string.Empty : " › " + section.HeadingPath)}\n\n{section.Text}";

    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>The form pgvector reads.</summary>
    public static string VectorText(float[] vector) =>
        "[" + string.Join(',', vector.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";

    /// <summary>Full-text search, in the section's language, without accents: customers often leave them out (ADR 0026 §3).</summary>
    public Task<int> IndexKeywordsAsync(Guid sourceId, CancellationToken ct) => db.Database.ExecuteSqlAsync(
        $"""
        UPDATE ai.knowledge_sections
        SET search_vector = to_tsvector(
            CASE language WHEN 'it' THEN 'italian'::regconfig WHEN 'en' THEN 'english'::regconfig ELSE 'simple'::regconfig END,
            unaccent(coalesce(heading_path, '') || ' ' || text))
        WHERE source_id = {sourceId}
        """,
        ct);

    /// <summary>The same text with the same model is copied from where it was already embedded, not paid for again.</summary>
    public async Task EmbedAsync(Guid sourceId, string title, IReadOnlyList<KnowledgeSection> sections, CancellationToken ct)
    {
        var model = embeddings.Model;
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE ai.knowledge_sections s
            SET embedding = o.embedding, embedding_model = o.embedding_model
            FROM ai.knowledge_sections o
            WHERE s.source_id = {sourceId} AND s.embedding IS NULL AND o.id <> s.id
              AND o.text_hash = s.text_hash AND o.embedding_model = {model} AND o.embedding IS NOT NULL
            """,
            ct);
        var ids = sections.Select(s => s.Id).ToList();
        var done = await db.Sections.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && s.EmbeddingModel == model)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var missing = sections.Where(s => !done.Contains(s.Id)).GroupBy(s => s.TextHash!).ToList();
        foreach (var batch in missing.Chunk(EmbeddingBatch))
        {
            var vectors = await embeddings.EmbedAsync([.. batch.Select(g => EmbeddingText(title, g.First()))], ct);
            for (var i = 0; i < batch.Length; i++)
            {
                var vector = VectorText(vectors[i]);
                var same = batch[i].Select(s => s.Id).ToList();
                await db.Database.ExecuteSqlAsync(
                    $"UPDATE ai.knowledge_sections SET embedding = CAST({vector} AS vector), embedding_model = {model} WHERE id = ANY({same})",
                    ct);
            }
        }
    }

    /// <summary>
    /// After an edit or merge. The old embedding goes first, so search never finds the old meaning.
    /// If the provider fails the text stays saved, found by keywords: returns false to say so.
    /// </summary>
    public async Task<bool> ReindexAsync(KnowledgeSource source, KnowledgeSection section, CancellationToken ct)
    {
        var id = section.Id;
        await db.Database.ExecuteSqlAsync($"UPDATE ai.knowledge_sections SET embedding = NULL, embedding_model = NULL WHERE id = {id}", ct);
        await IndexKeywordsAsync(source.Id, ct);
        if (!embeddings.IsConfigured)
        {
            return false;
        }

        try
        {
            await EmbedAsync(source.Id, source.Title, [section], ct);
            return true;
        }
        catch (LlmException)
        {
            return false;
        }
    }
}
