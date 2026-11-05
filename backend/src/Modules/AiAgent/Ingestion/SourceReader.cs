using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.Modules.AiAgent.Ingestion;

/// <summary>
/// Reads one source in its workspace's scope (ADR 0026 §2): get the content, parse, split, store,
/// index, embed; ends in <c>review</c> or <c>failed</c>. Logs carry IDs and codes, never content (SEC-19).
/// </summary>
internal sealed partial class SourceReader(
    AiDbContext db,
    IFileStore files,
    SafeHttpClient web,
    IEmbeddingProvider embeddings,
    SectionIndex index,
    TimeProvider time,
    ILogger<SourceReader> logger)
{
    public const int MaxAttempts = 3;
    public const long MaxPageBytes = 5 * 1024 * 1024;

    /// <summary>A crashed job's source is free again after this; also the most one read may take.</summary>
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(10);

    /// <summary>
    /// SEC-04: parsing is limited in time. The parsers can't be cancelled, so a stuck one is left
    /// behind on its thread; the source fails and the job goes on.
    /// </summary>
    public static readonly TimeSpan ParseTimeout = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    public async Task ReadAsync(Guid sourceId, CancellationToken ct)
    {
        // Atomic, so two jobs never read the same source.
        var now = time.GetUtcNow();
        var lockUntil = now + LockFor;
        var claimed = await db.Sources
            .Where(s => s.Id == sourceId && s.Status == SourceStatus.Reading && (s.ReadLockUntil == null || s.ReadLockUntil <= now))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.ReadLockUntil, lockUntil).SetProperty(s => s.ReadAttempts, s => s.ReadAttempts + 1), ct);
        if (claimed == 0)
        {
            return;
        }

        var source = await db.Sources.SingleAsync(s => s.Id == sourceId, ct);
        try
        {
            await ReadContentAsync(source, ct);
        }
        catch (SourceReadException ex)
        {
            await FailAsync(source, ex.Code, ct);
        }
        catch (SafeHttpException ex)
        {
            await WebFailureAsync(source, ex, ct);
        }
        catch (LlmException ex)
        {
            LogEmbeddingFailed(logger, source.Id, ex.Reason);
            await RetryOrFailAsync(source, FailureCodes.EmbeddingFailed, ct);
        }
    }

    private async Task ReadContentAsync(KnowledgeSource source, CancellationToken ct)
    {
        var content = source.Kind == SourceKind.WebPage ? await FetchPageAsync(source, ct) : await ReadFileAsync(source, ct);
        var document = await ParseAsync(source.Kind, content, source.Origin, ct);
        var chunks = Chunker.Split(document.Sections);
        if (chunks.Count == 0)
        {
            throw new SourceReadException(document.Pages is > 0 ? FailureCodes.Scanned : FailureCodes.NoText);
        }

        // Read again from the start after a retry: the old sections of this source go.
        await db.Sections.Where(s => s.SourceId == source.Id).ExecuteDeleteAsync(ct);
        var sections = new List<KnowledgeSection>();
        // Short sections have too few words to tell: they get the language of the whole document.
        var documentLanguage = LanguageGuess.Of(string.Join(' ', chunks.Select(c => c.Text)), document.Language);
        foreach (var (chunk, position) in chunks.Select((c, i) => (c, i)))
        {
            var section = KnowledgeSection.Create(
                source.TenantId, source.Id, position, chunk.HeadingPath, chunk.Page, chunk.Kind, chunk.Text, chunk.Tokens,
                LanguageGuess.Of(chunk.Text, documentLanguage));
            section.Hashed(SectionIndex.Hash(SectionIndex.EmbeddingText(document.Title ?? source.Title, section)));
            sections.Add(section);
        }

        db.Sections.AddRange(sections);
        await db.SaveChangesAsync(ct);
        await index.IndexKeywordsAsync(source.Id, ct);

        var warnings = new List<string>();
        if (document.PagesWithoutText.Count > 0)
        {
            warnings.Add($"pages_without_text:{string.Join(',', document.PagesWithoutText)}");
        }

        if (embeddings.IsConfigured)
        {
            await index.EmbedAsync(source.Id, document.Title ?? source.Title, sections, ct);
        }
        else
        {
            warnings.Add("no_embeddings");
        }

        source.ReadDone(document.Title, document.Pages, warnings, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    private async Task<byte[]> ReadFileAsync(KnowledgeSource source, CancellationToken ct)
    {
        try
        {
            await using var file = await files.OpenReadAsync(source.StorageKey!, ct);
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new SourceReadException(FailureCodes.ReadFailed);
        }
    }

    private async Task<byte[]> FetchPageAsync(KnowledgeSource source, CancellationToken ct)
    {
        var page = await web.GetAsync(new Uri(source.Origin), MaxPageBytes, ct);
        if (page.ContentType is not (null or "text/html" or "application/xhtml+xml"))
        {
            throw new SourceReadException(FailureCodes.NotAPage);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(page.Body));
        if (await db.Sources.AnyAsync(s => s.Id != source.Id && s.ContentHash == hash, ct))
        {
            throw new SourceReadException(FailureCodes.Duplicate);
        }

        source.PageContent(hash, page.Body.Length);
        return page.Body;
    }

    private static async Task<ParsedDocument> ParseAsync(SourceKind kind, byte[] content, string origin, CancellationToken ct)
    {
        IDocumentParser parser = kind switch
        {
            SourceKind.Pdf => new PdfParser(),
            SourceKind.Docx => new DocxParser(),
            SourceKind.WebPage => new WebPageParser(),
            _ => new TextParser(),
        };

        try
        {
            return await Task.Run(() => parser.Parse(content, origin), ct).WaitAsync(ParseTimeout, ct);
        }
        catch (TimeoutException)
        {
            throw new SourceReadException(FailureCodes.TooComplex);
        }
    }

    private Task WebFailureAsync(KnowledgeSource source, SafeHttpException ex, CancellationToken ct) => ex.Failure switch
    {
        SafeHttpFailure.BlockedAddress => FailAsync(source, FailureCodes.BlockedUrl, ct),
        SafeHttpFailure.InvalidUrl or SafeHttpFailure.TooManyRedirects => FailAsync(source, FailureCodes.InvalidUrl, ct),
        SafeHttpFailure.TooLarge => FailAsync(source, FailureCodes.PageTooLarge, ct),
        // Sites come back; three tries before saying so.
        _ => RetryOrFailAsync(source, FailureCodes.PageUnavailable, ct),
    };

    private async Task RetryOrFailAsync(KnowledgeSource source, string code, CancellationToken ct)
    {
        if (source.ReadAttempts >= MaxAttempts)
        {
            await FailAsync(source, code, ct);
            return;
        }

        source.RetryAt(time.GetUtcNow() + RetryAfter * source.ReadAttempts);
        await db.SaveChangesAsync(ct);
    }

    private async Task FailAsync(KnowledgeSource source, string code, CancellationToken ct)
    {
        LogFailed(logger, source.Id, code);
        await db.Sections.Where(s => s.SourceId == source.Id).ExecuteDeleteAsync(ct);
        source.ReadFailed(code, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {SourceId} could not be read ({Code})")]
    private static partial void LogFailed(ILogger logger, Guid sourceId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Embeddings failed for source {SourceId} ({Reason})")]
    private static partial void LogEmbeddingFailed(ILogger logger, Guid sourceId, string reason);
}

/// <summary>
/// Reads waiting sources in the Workers host, so a big PDF never slows the API (ADR 0026 §2). Each
/// source has its own scope with its workspace set; one failure never stops the others.
/// </summary>
internal sealed partial class SourceReadProcessor(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<AiOptions> options,
    ILogger<SourceReadProcessor> logger) : BackgroundService
{
    public const int BatchSize = 5;

    /// <summary>Reads one batch; returns how many sources were picked up. Tests call it directly.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        List<DueRow> due;
        var now = time.GetUtcNow();
        await using (var lookup = scopes.CreateAsyncScope())
        {
            // SECURITY DEFINER lookup (migration AddSourceReading): IDs only.
            due = await lookup.ServiceProvider.GetRequiredService<AiDbContext>().Database
                .SqlQuery<DueRow>($"SELECT tenant_id, source_id FROM ai.due_sources({now}, {BatchSize})")
                .ToListAsync(ct);
        }

        foreach (var item in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(item.TenantId);
                await scope.ServiceProvider.GetRequiredService<SourceReader>().ReadAsync(item.SourceId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ex.GetType().Name;
                LogFailed(logger, item.SourceId, reason);
                await GiveUpAfterLastAttemptAsync(item, ct);
            }
        }

        return due.Count;
    }

    /// <summary>An unexpected error: tried again when the lock ends, and after the last attempt the owner is told.</summary>
    private async Task GiveUpAfterLastAttemptAsync(DueRow item, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(item.TenantId);
        await scope.ServiceProvider.GetRequiredService<AiDbContext>().Sources
            .Where(s => s.Id == item.SourceId && s.Status == SourceStatus.Reading && s.ReadAttempts >= SourceReader.MaxAttempts)
            .ExecuteUpdateAsync(
                u => u.SetProperty(s => s.Status, SourceStatus.Failed)
                    .SetProperty(s => s.FailureReason, FailureCodes.ReadFailed)
                    .SetProperty(s => s.ReadLockUntil, (DateTimeOffset?)null),
                ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var picked = 0;
            try
            {
                picked = await ProcessOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ex.GetType().Name;
                LogBatchFailed(logger, reason);
            }

            if (picked < BatchSize)
            {
                await Task.Delay(options.Value.PollInterval, time, stoppingToken);
            }
        }
    }

    private sealed record DueRow(Guid TenantId, Guid SourceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading source {SourceId} failed ({Error}); tried again later")]
    private static partial void LogFailed(ILogger logger, Guid sourceId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source reading batch failed ({Error}); retrying later")]
    private static partial void LogBatchFailed(ILogger logger, string error);
}
