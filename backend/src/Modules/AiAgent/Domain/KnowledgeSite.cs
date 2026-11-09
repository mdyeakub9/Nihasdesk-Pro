using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Domain;

internal enum SiteStatus
{
    Finding,
    Ready,
    Failed,
}

/// <summary>A page found on a site. <see cref="Chosen"/>: a main page, ticked for the owner. The title comes from the site: plain text only.</summary>
internal sealed record FoundPage(string Url, string Title, bool Chosen);

/// <summary>
/// A website the owner gave by its domain (Issue #80, ADR 0030). A job finds its pages; the owner
/// adds the ones they want, and each becomes an ordinary web page source that points back here.
/// </summary>
internal sealed class KnowledgeSite : TenantEntity
{
    public const int HostMaxLength = 255;
    public const int FailureMaxLength = 60;
    public const int MaxPerWorkspace = 10;

    private KnowledgeSite(Guid tenantId, string host, string rootUrl, Guid createdByUserId)
        : base(tenantId)
    {
        Host = host;
        RootUrl = rootUrl;
        CreatedByUserId = createdByUserId;
    }

    /// <summary>Without "www.", so the same site isn't added twice.</summary>
    public string Host { get; private set; }

    public string RootUrl { get; private set; }

    public SiteStatus Status { get; private set; } = SiteStatus.Finding;

    public string? FailureReason { get; private set; }

    public List<FoundPage> Pages { get; private set; } = [];

    public int FindAttempts { get; private set; }

    public DateTimeOffset? FindLockUntil { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public static KnowledgeSite Create(Guid tenantId, string host, string rootUrl, Guid userId) => new(tenantId, host, rootUrl, userId);

    public void Claim(DateTimeOffset lockUntil)
    {
        FindLockUntil = lockUntil;
        FindAttempts++;
    }

    public void Found(IReadOnlyList<FoundPage> pages)
    {
        Pages = [.. pages];
        Status = SiteStatus.Ready;
        FailureReason = null;
        FindLockUntil = null;
    }

    public void Failed(string reason)
    {
        Status = SiteStatus.Failed;
        FailureReason = reason.Length <= FailureMaxLength ? reason : reason[..FailureMaxLength];
        FindLockUntil = null;
    }
}
