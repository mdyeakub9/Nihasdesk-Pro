using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Nihasdesk.Modules.AiAgent.Domain;

namespace Nihasdesk.Modules.AiAgent.Ingestion;

/// <summary>A page a site mentions. <see cref="Text"/>: what its link says, when known. <see cref="InMenu"/>: linked from a menu or footer.</summary>
internal sealed record SiteLink(Uri Url, string? Text, bool InMenu);

/// <summary>What a domain the owner typed means: one public-looking site, nothing else from the text is used.</summary>
internal static class SiteAddress
{
    public static bool TryParse(string? text, out string host, out Uri root)
    {
        host = string.Empty;
        root = null!;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > KnowledgeSite.HostMaxLength)
        {
            return false;
        }

        var withScheme = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            || !url.IsDefaultPort
            || !url.Host.Contains('.', StringComparison.Ordinal)
            || !string.IsNullOrEmpty(url.UserInfo))
        {
            return false;
        }

        host = WithoutWww(url.IdnHost);
        root = new Uri($"{url.Scheme}://{url.IdnHost}/");
        return true;
    }

    public static string WithoutWww(string host)
    {
        var lower = host.ToLowerInvariant();
        return lower.StartsWith("www.", StringComparison.Ordinal) ? lower[4..] : lower;
    }
}

/// <summary>A site's robots.txt, for our crawler or for everyone: the longest matching rule decides, as the standard says (RFC 9309).</summary>
internal sealed class RobotsRules
{
    private const string OurAgent = "nihasdeskbot";
    private const int MaxRules = 500;

    private readonly List<(bool Allow, string Pattern, Regex Match)> _rules = [];

    public List<string> Sitemaps { get; } = [];

    public static RobotsRules Parse(string? text)
    {
        var rules = new RobotsRules();
        if (string.IsNullOrWhiteSpace(text))
        {
            return rules;
        }

        var ours = new List<(bool, string)>();
        var everyone = new List<(bool, string)>();
        var agents = new List<string>();
        var inRules = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "sitemap":
                    rules.Sitemaps.Add(value);
                    break;
                case "user-agent":
                    if (inRules)
                    {
                        agents.Clear();
                        inRules = false;
                    }

                    agents.Add(value.ToLowerInvariant());
                    break;
                case "allow" or "disallow":
                    inRules = true;
                    // An empty Disallow allows everything: no rule.
                    if (value.Length > 0)
                    {
                        if (agents.Contains(OurAgent))
                        {
                            ours.Add((key == "allow", value));
                        }

                        if (agents.Contains("*"))
                        {
                            everyone.Add((key == "allow", value));
                        }
                    }

                    break;
            }
        }

        foreach (var (allow, pattern) in (ours.Count > 0 ? ours : everyone).Take(MaxRules))
        {
            // The site writes these patterns: no backtracking, so one can't stall the job.
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\$", "$", StringComparison.Ordinal);
            rules._rules.Add((allow, pattern, new Regex(regex, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)));
        }

        return rules;
    }

    public bool Allows(string path)
    {
        var best = -1;
        var allowed = true;
        foreach (var (allow, pattern, match) in _rules)
        {
            if (match.IsMatch(path) && (pattern.Length > best || (pattern.Length == best && allow)))
            {
                best = pattern.Length;
                allowed = allow;
            }
        }

        return allowed;
    }
}

/// <summary>Reads links out of what a site sends. Everything here is untrusted text from the internet.</summary>
internal static class SiteParsers
{
    private const int MaxLinks = 2000;

    /// <summary>The addresses in a sitemap, and whether they are more sitemaps. Bad XML is no sitemap; a DTD is never processed (SEC-05).</summary>
    public static (IReadOnlyList<string> Urls, bool IsIndex) Sitemap(byte[] body)
    {
        try
        {
            using var stream = new MemoryStream(body);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 });
            var document = XDocument.Load(reader);
            var urls = document.Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value.Trim()).Take(MaxLinks).ToList();
            return (urls, document.Root?.Name.LocalName == "sitemapindex");
        }
        catch (XmlException)
        {
            return ([], false);
        }
    }

    public static IReadOnlyList<SiteLink> Links(string html, Uri page)
    {
        var document = new HtmlParser().ParseDocument(html);
        var links = new List<SiteLink>();
        foreach (var anchor in document.QuerySelectorAll("a[href]").Take(MaxLinks))
        {
            if (Uri.TryCreate(page, anchor.GetAttribute("href"), out var url))
            {
                var text = Regex.Replace(anchor.TextContent, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
                links.Add(new SiteLink(url, text.Length == 0 ? null : text, anchor.Closest("nav, header, footer, [role=navigation]") is not null));
            }
        }

        return links;
    }
}

/// <summary>
/// Which of a site's pages answer customers' questions, by code (ADR 0030): words in the link and
/// the address, English and Italian. Products come from the catalog, so their pages aren't taken.
/// </summary>
internal static partial class PageChooser
{
    public const int MaxChosen = 15;
    public const int MaxListed = 100;

    private const int KeywordPoints = 10;

    // At the start of a word: "spedizion" is "spedizioni" and "spedizione".
    private static readonly string[] Keywords =
    [
        "about", "our story", "faq", "help", "support", "shipping", "delivery", "return", "refund", "exchange", "payment", "contact",
        "terms", "warranty", "guarantee", "size guide", "how to order", "store location", "opening hours",
        "chi siamo", "azienda", "la nostra storia", "domande", "aiuto", "assistenza", "spedizion", "consegn", "resi", "reso", "rimbors",
        "pagament", "contatt", "condizioni", "termini", "garanzia", "guida alle taglie", "come ordinare", "dove siamo", "orari",
    ];

    // Whole parts of the address: "shop" is not "workshop".
    private static readonly HashSet<string> NotThese = new(StringComparer.Ordinal)
    {
        "product", "products", "prodotto", "prodotti", "collection", "collections", "collezione", "collezioni", "shop", "store", "catalog",
        "catalogo", "cart", "carrello", "checkout", "cassa", "account", "login", "signin", "signup", "register", "registrati", "accedi",
        "search", "cerca", "tag", "tags", "category", "categories", "categoria", "categorie", "blog", "blogs", "news", "post", "posts",
        "article", "articles", "articolo", "articoli", "wishlist", "compare", "admin", "wp-admin", "wp-login", "wp-json", "feed", "page",
        "privacy", "cookie", "cookies", "cdn", "assets",
    };

    private static readonly HashSet<string> PageExtensions = new(StringComparer.OrdinalIgnoreCase) { string.Empty, ".html", ".htm", ".php", ".asp", ".aspx" };

    /// <summary>The same site, with or without "www."; subdomains are other sites.</summary>
    public static bool OnSite(Uri url, string host) =>
        (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) && url.IsDefaultPort && SiteAddress.WithoutWww(url.IdnHost) == host;

    /// <summary>The address to keep for a link on this site, or null: other sites, files and addresses with a query are left out.</summary>
    public static Uri? Clean(Uri url, string host)
    {
        if (!OnSite(url, host)
            || url.Query.Length > 0
            || !PageExtensions.Contains(Path.GetExtension(url.AbsolutePath)))
        {
            return null;
        }

        return new UriBuilder(url) { Fragment = string.Empty }.Uri;
    }

    public static bool IsExcluded(Uri url) =>
        url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part =>
            NotThese.Contains(part.ToLowerInvariant()) || YearOrNumber().IsMatch(part));

    public static int Score(SiteLink link)
    {
        var words = " " + Spaced().Replace($"{link.Text} {Uri.UnescapeDataString(link.Url.AbsolutePath)}".ToLowerInvariant(), " ") + " ";
        var depth = link.Url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
        return (Keywords.Any(k => words.Contains(" " + k, StringComparison.Ordinal)) ? KeywordPoints : 0)
            + (link.InMenu ? 2 : 0)
            + (depth <= 2 ? 1 : 0)
            - (depth >= 4 ? 2 : 0);
    }

    /// <summary>The home page, then the main pages (ticked), then the rest for the owner to tick if they want.</summary>
    public static IReadOnlyList<FoundPage> Choose(Uri home, IEnumerable<SiteLink> links, RobotsRules robots, string host)
    {
        var pages = new Dictionary<string, SiteLink>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            if (Clean(link.Url, host) is not { } url || IsExcluded(url) || !robots.Allows(url.AbsolutePath))
            {
                continue;
            }

            var key = url.AbsolutePath.TrimEnd('/');
            if (key.Length == 0)
            {
                continue;
            }

            // The same page twice: keep what its link says, and that a menu links to it.
            var known = pages.GetValueOrDefault(key);
            pages[key] = new SiteLink(known?.Url ?? url, known?.Text ?? link.Text, link.InMenu || known?.InMenu == true);
        }

        var ranked = pages.Values
            .Select(p => (Link: p, Points: Score(p)))
            .OrderByDescending(p => p.Points)
            .ThenBy(p => p.Link.Url.AbsolutePath, StringComparer.Ordinal)
            .ToList();
        var chosen = ranked.Where(p => p.Points >= KeywordPoints).Take(MaxChosen - 1).ToList();
        var found = new List<FoundPage> { new(home.AbsoluteUri, "Home", true) };
        found.AddRange(chosen.Select(p => new FoundPage(p.Link.Url.AbsoluteUri, Title(p.Link), true)));
        found.AddRange(ranked.Except(chosen).Take(MaxListed - found.Count).Select(p => new FoundPage(p.Link.Url.AbsoluteUri, Title(p.Link), false)));
        return found;
    }

    private static string Title(SiteLink link)
    {
        var last = link.Url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? link.Url.AbsoluteUri;
        var title = string.IsNullOrWhiteSpace(link.Text) ? Spaced().Replace(Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(last)), " ").Trim() : link.Text;
        return title.Length <= KnowledgeSource.TitleMaxLength ? title : title[..KnowledgeSource.TitleMaxLength];
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Spaced();

    // Dates in blog addresses and page numbers.
    [GeneratedRegex(@"^\d+$")]
    private static partial Regex YearOrNumber();
}
