using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.Modules.AiAgent.Ingestion;

/// <summary>
/// Finds a site's pages (ADR 0030): robots.txt first, then its sitemap, otherwise the links on the
/// home page and on its most promising pages. Only reads addresses: the pages themselves are read
/// later, one at a time, once the owner added them. Every request goes through <see cref="SafeHttpClient"/> (SEC-05).
/// </summary>
internal sealed partial class SiteFinder(
    AiDbContext db,
    SafeHttpClient web,
    IOptions<AiOptions> options,
    TimeProvider time,
    ILogger<SiteFinder> logger)
{
    public const int MaxAttempts = 3;
    public const int MaxRequests = 60;
    public const long MaxBytes = 5 * 1024 * 1024;

    private const int MaxChildSitemaps = 3;
    private const int MaxHubPages = 10;

    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(10);

    private int _requests;

    public async Task RunAsync(Guid siteId, CancellationToken ct)
    {
        var site = await db.Sites.SingleOrDefaultAsync(s => s.Id == siteId, ct);
        var now = time.GetUtcNow();
        if (site is null || site.Status != SiteStatus.Finding || site.FindLockUntil > now)
        {
            return;
        }

        site.Claim(now.Add(LockFor));
        await db.SaveChangesAsync(ct);
        try
        {
            await FindAsync(site, ct);
        }
        catch (SafeHttpException ex) when (ex.Failure == SafeHttpFailure.BlockedAddress)
        {
            site.Failed(SiteFailures.Blocked);
        }
        catch (SafeHttpException ex)
        {
            var reason = ex.Failure.ToString();
            LogFailed(logger, siteId, reason);
            if (site.FindAttempts >= MaxAttempts)
            {
                site.Failed(SiteFailures.Unreachable);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task FindAsync(KnowledgeSite site, CancellationToken ct)
    {
        var root = new Uri(site.RootUrl);
        var robots = RobotsRules.Parse(await TextOrNullAsync(new Uri(root, "/robots.txt"), ct));
        if (!robots.Allows("/"))
        {
            site.Failed(SiteFailures.RobotsForbid);
            return;
        }

        var home = await FetchAsync(root, ct);
        if (home.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) != true)
        {
            site.Failed(SiteFailures.NotHtml);
            return;
        }

        // A domain may lead to another address of the same shop: that is the site from here on.
        var host = SiteAddress.WithoutWww(home.FinalUrl.IdnHost);
        var links = SiteParsers.Links(Encoding.UTF8.GetString(home.Body), home.FinalUrl).ToList();
        var fromSitemap = await SitemapLinksAsync(root, robots, host, ct);
        links.AddRange(fromSitemap);
        if (fromSitemap.Count == 0)
        {
            links.AddRange(await HubLinksAsync(links, robots, host, ct));
        }

        site.Found(PageChooser.Choose(home.FinalUrl, links, robots, host));
    }

    private async Task<List<SiteLink>> SitemapLinksAsync(Uri root, RobotsRules robots, string host, CancellationToken ct)
    {
        var listed = robots.Sitemaps
            .Select(s => Uri.TryCreate(s, UriKind.Absolute, out var url) && PageChooser.OnSite(url, host) ? url : null)
            .OfType<Uri>()
            .Distinct()
            .ToList();
        if (listed.Count == 0)
        {
            listed.Add(new Uri(root, "/sitemap.xml"));
        }

        var links = new List<SiteLink>();
        foreach (var sitemap in listed.Take(MaxChildSitemaps))
        {
            var (urls, isIndex) = await SitemapAsync(sitemap, ct);
            if (!isIndex)
            {
                links.AddRange(Absolute(urls));
                continue;
            }

            // Shops split them: pages first, products aren't wanted anyway.
            var children = urls
                .OrderBy(u => u.Contains("page", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .Where(u => !u.Contains("product", StringComparison.OrdinalIgnoreCase) && !u.Contains("blog", StringComparison.OrdinalIgnoreCase) && !u.Contains("collection", StringComparison.OrdinalIgnoreCase))
                .Take(MaxChildSitemaps);
            foreach (var child in children)
            {
                if (Uri.TryCreate(child, UriKind.Absolute, out var url) && PageChooser.OnSite(url, host))
                {
                    links.AddRange(Absolute((await SitemapAsync(url, ct)).Urls));
                }
            }
        }

        return links;

        static IEnumerable<SiteLink> Absolute(IEnumerable<string> urls) =>
            urls.Select(u => Uri.TryCreate(u, UriKind.Absolute, out var url) ? new SiteLink(url, null, false) : null).OfType<SiteLink>();
    }

    private async Task<(IReadOnlyList<string> Urls, bool IsIndex)> SitemapAsync(Uri url, CancellationToken ct)
    {
        try
        {
            return SiteParsers.Sitemap((await FetchAsync(url, ct)).Body);
        }
        catch (SafeHttpException ex) when (ex.Failure is SafeHttpFailure.BadStatus or SafeHttpFailure.TooLarge or SafeHttpFailure.TooManyRedirects)
        {
            return ([], false);
        }
    }

    /// <summary>No sitemap: the links one step further, from the pages most likely to lead to the others ("Help" to its FAQ).</summary>
    private async Task<List<SiteLink>> HubLinksAsync(List<SiteLink> links, RobotsRules robots, string host, CancellationToken ct)
    {
        var hubs = links
            .Select(l => (Link: l, Url: PageChooser.Clean(l.Url, host)))
            .Where(l => l.Url is not null && l.Url.AbsolutePath.Trim('/').Length > 0 && !PageChooser.IsExcluded(l.Url) && robots.Allows(l.Url.AbsolutePath))
            .OrderByDescending(l => PageChooser.Score(l.Link))
            .Select(l => l.Url!)
            .DistinctBy(u => u.AbsolutePath.TrimEnd('/'))
            .Take(MaxHubPages)
            .ToList();
        var more = new List<SiteLink>();
        foreach (var hub in hubs)
        {
            if (_requests >= MaxRequests)
            {
                break;
            }

            try
            {
                var page = await FetchAsync(hub, ct);
                if (page.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
                {
                    more.AddRange(SiteParsers.Links(Encoding.UTF8.GetString(page.Body), page.FinalUrl));
                }
            }
            catch (SafeHttpException ex) when (ex.Failure != SafeHttpFailure.BlockedAddress)
            {
                // One broken link doesn't stop the rest.
            }
        }

        return more;
    }

    /// <summary>A site without robots.txt allows everything; one that answers with an error is treated the same way.</summary>
    private async Task<string?> TextOrNullAsync(Uri url, CancellationToken ct)
    {
        try
        {
            return Encoding.UTF8.GetString((await FetchAsync(url, ct)).Body);
        }
        catch (SafeHttpException ex) when (ex.Failure == SafeHttpFailure.BadStatus)
        {
            return null;
        }
    }

    private async Task<SafeHttpResponse> FetchAsync(Uri url, CancellationToken ct)
    {
        if (_requests >= MaxRequests)
        {
            throw new SafeHttpException(SafeHttpFailure.BadStatus, "Too many requests for one site.");
        }

        if (_requests++ > 0)
        {
            // Polite to a small shop's server.
            await Task.Delay(options.Value.SiteRequestDelay, time, ct);
        }

        return await web.GetAsync(url, MaxBytes, ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Finding the pages of site {SiteId} failed ({Reason})")]
    private static partial void LogFailed(ILogger logger, Guid siteId, string reason);
}

/// <summary>Why a site's pages couldn't be found; the dashboard says it in plain words.</summary>
internal static class SiteFailures
{
    public const string Unreachable = "site_unreachable";
    public const string Blocked = "site_blocked";
    public const string RobotsForbid = "site_robots_forbid";
    public const string NotHtml = "site_not_html";
}

/// <summary>Runs waiting sites in the Workers host. Each has its own scope with its workspace set; one failure never stops the others.</summary>
internal sealed partial class SiteFindProcessor(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<AiOptions> options,
    ILogger<SiteFindProcessor> logger) : BackgroundService
{
    public const int BatchSize = 5;

    /// <summary>Runs one batch; returns how many sites were picked up. Tests call it directly.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        List<DueRow> due;
        var now = time.GetUtcNow();
        await using (var lookup = scopes.CreateAsyncScope())
        {
            // SECURITY DEFINER lookup (migration AddKnowledgeSites): IDs only.
            due = await lookup.ServiceProvider.GetRequiredService<AiDbContext>().Database
                .SqlQuery<DueRow>($"SELECT tenant_id, site_id FROM ai.due_sites({now}, {BatchSize})")
                .ToListAsync(ct);
        }

        foreach (var item in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(item.TenantId);
                await scope.ServiceProvider.GetRequiredService<SiteFinder>().RunAsync(item.SiteId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ex.GetType().Name;
                LogFailed(logger, item.SiteId, reason);
                await GiveUpAfterLastAttemptAsync(item, ct);
            }
        }

        return due.Count;
    }

    /// <summary>A bad page must not leave the owner waiting for ever.</summary>
    private async Task GiveUpAfterLastAttemptAsync(DueRow item, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(item.TenantId);
        await scope.ServiceProvider.GetRequiredService<AiDbContext>().Sites
            .Where(s => s.Id == item.SiteId && s.Status == SiteStatus.Finding && s.FindAttempts >= SiteFinder.MaxAttempts)
            .ExecuteUpdateAsync(
                u => u.SetProperty(s => s.Status, SiteStatus.Failed)
                    .SetProperty(s => s.FailureReason, SiteFailures.Unreachable)
                    .SetProperty(s => s.FindLockUntil, (DateTimeOffset?)null),
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

    private sealed record DueRow(Guid TenantId, Guid SiteId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Finding the pages of site {SiteId} failed ({Error}); tried again later")]
    private static partial void LogFailed(ILogger logger, Guid siteId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Site finding batch failed ({Error}); retrying later")]
    private static partial void LogBatchFailed(ILogger logger, string error);
}
