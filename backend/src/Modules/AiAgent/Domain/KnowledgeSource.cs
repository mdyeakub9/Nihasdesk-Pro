using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Domain;

internal enum SourceKind
{
    Pdf,
    Docx,
    Txt,
    WebPage,
}

/// <summary>Reading → Review → Live, or Failed with a reason (PRD 4.7).</summary>
internal enum SourceStatus
{
    Reading,
    Review,
    Live,
    Failed,
    Archived,
}

/// <summary>
/// A document or web page the shop gave the AI (ADR 0026 §1). The raw file stays in the file store
/// under <see cref="StorageKey"/>; the AI only ever gets its sections.
/// </summary>
internal sealed class KnowledgeSource : TenantEntity
{
    public const int TitleMaxLength = 255;
    public const int OriginMaxLength = 2048;
    public const int HashLength = 64;
    public const int StorageKeyMaxLength = 80;
    public const int FailureMaxLength = 60;

    /// <summary>Limits of PRD 4.7.</summary>
    public const int MaxPerWorkspace = 100;
    public const long MaxFileBytes = 20 * 1024 * 1024;

    private KnowledgeSource(Guid tenantId, SourceKind kind, string title, string origin, Guid createdByUserId)
        : base(tenantId)
    {
        Kind = kind;
        Title = title;
        Origin = origin;
        CreatedByUserId = createdByUserId;
    }

    public SourceKind Kind { get; private set; }

    /// <summary>The website this page was added from (ADR 0030); deleting the site deletes it.</summary>
    public Guid? SiteId { get; private set; }

    public string Title { get; private set; }

    /// <summary>The file name as uploaded (shown only, never a path) or the page's URL.</summary>
    public string Origin { get; private set; }

    public string? StorageKey { get; private set; }

    /// <summary>SHA-256 of the content, to notice the same file twice. Null until a page is read.</summary>
    public string? ContentHash { get; private set; }

    public long? SizeBytes { get; private set; }

    public SourceStatus Status { get; private set; } = SourceStatus.Reading;

    /// <summary>A code the dashboard turns into plain words; never file content.</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset? LastReadAt { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    /// <summary>How often the reading job started on this source.</summary>
    public int ReadAttempts { get; private set; }

    /// <summary>A job holds the source until then; after a failure, the next attempt's time.</summary>
    public DateTimeOffset? ReadLockUntil { get; private set; }

    public int? PageCount { get; private set; }

    /// <summary>What the owner should know, as codes: <c>pages_without_text:4,5</c>, <c>no_embeddings</c>.</summary>
    public List<string> Warnings { get; private set; } = [];

    public void ReadDone(string? title, int? pageCount, IEnumerable<string> warnings, DateTimeOffset now)
    {
        // A page's address is only a stand-in for its title; a file keeps the name the owner knows.
        if (Kind == SourceKind.WebPage && !string.IsNullOrWhiteSpace(title))
        {
            Title = Shorten(title, TitleMaxLength);
        }

        PageCount = pageCount;
        Warnings = [.. warnings];
        Status = SourceStatus.Review;
        FailureReason = null;
        LastReadAt = now;
        ReadLockUntil = null;
    }

    public void ReadFailed(string reason, DateTimeOffset now)
    {
        Status = SourceStatus.Failed;
        FailureReason = Shorten(reason, FailureMaxLength);
        LastReadAt = now;
        ReadLockUntil = null;
    }

    /// <summary>The owner lets the AI use it. Replaced by draft and publish in Issue #55.</summary>
    public bool Use()
    {
        if (Status is not (SourceStatus.Review or SourceStatus.Live))
        {
            return false;
        }

        Status = SourceStatus.Live;
        return true;
    }

    public bool StopUsing()
    {
        if (Status is not (SourceStatus.Review or SourceStatus.Live))
        {
            return false;
        }

        Status = SourceStatus.Review;
        return true;
    }

    /// <summary>A failure that may pass (network, provider): tried again from then on.</summary>
    public void RetryAt(DateTimeOffset at) => ReadLockUntil = at;

    public void PageContent(string contentHash, long sizeBytes)
    {
        ContentHash = contentHash;
        SizeBytes = sizeBytes;
    }

    public static KnowledgeSource FromFile(Guid tenantId, SourceKind kind, string fileName, string storageKey, string contentHash, long sizeBytes, Guid userId) =>
        new(tenantId, kind, Shorten(fileName, TitleMaxLength), Shorten(fileName, OriginMaxLength), userId)
        {
            StorageKey = storageKey,
            ContentHash = contentHash,
            SizeBytes = sizeBytes,
        };

    public static KnowledgeSource FromWebPage(Guid tenantId, string url, Guid userId, Guid? siteId = null) =>
        new(tenantId, SourceKind.WebPage, Shorten(url, TitleMaxLength), url, userId) { SiteId = siteId };

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max];
}

internal enum SectionKind
{
    Text,
    List,
    Table,
    Faq,
}

/// <summary>
/// One searchable part of a source (ADR 0026 §1–2). The columns <c>embedding</c> and
/// <c>search_vector</c> exist in the table but not here: only search SQL uses them.
/// </summary>
internal sealed class KnowledgeSection : TenantEntity
{
    public const int HeadingMaxLength = 500;
    public const int TextMaxLength = 20_000;
    public const int LanguageMaxLength = 10;
    public const int ModelMaxLength = 60;

    private KnowledgeSection(Guid tenantId, Guid sourceId, int position, string text)
        : base(tenantId)
    {
        SourceId = sourceId;
        Position = position;
        Text = text;
    }

    public Guid SourceId { get; private set; }

    /// <summary>Order within the source.</summary>
    public int Position { get; private set; }

    /// <summary>E.g. "Shipping › Italy › Islands".</summary>
    public string? HeadingPath { get; private set; }

    public int? Page { get; private set; }

    public SectionKind Kind { get; private set; }

    public string Text { get; private set; }

    public int TokenCount { get; private set; }

    /// <summary>The owner said the AI should not use this (PRD 4.7).</summary>
    public bool Excluded { get; private set; }

    public string? Language { get; private set; }

    /// <summary>Which model made the embedding, so a model change can re-embed in the background.</summary>
    public string? EmbeddingModel { get; private set; }

    /// <summary>SHA-256 of what is embedded: the same text isn't embedded (and paid for) twice.</summary>
    public string? TextHash { get; private set; }

    public void Hashed(string textHash) => TextHash = textHash;

    /// <summary>The owner corrected the text. Its search data is made again by the caller.</summary>
    public void Edit(string text, int tokenCount, string? language)
    {
        Text = text;
        TokenCount = tokenCount;
        Language = language;
        EmbeddingModel = null;
    }

    public void SetExcluded(bool excluded) => Excluded = excluded;

    /// <summary>Takes in the next section's text; the kind is plain text when the two differ.</summary>
    public void Merge(KnowledgeSection next, int tokenCount)
    {
        Text = Text + "\n\n" + next.Text;
        TokenCount = tokenCount;
        Kind = Kind == next.Kind ? Kind : SectionKind.Text;
        EmbeddingModel = null;
    }

    public static KnowledgeSection Create(Guid tenantId, Guid sourceId, int position, string? headingPath, int? page, SectionKind kind, string text, int tokenCount, string? language) =>
        new(tenantId, sourceId, position, text)
        {
            HeadingPath = headingPath,
            Page = page,
            Kind = kind,
            TokenCount = tokenCount,
            Language = language,
        };
}
