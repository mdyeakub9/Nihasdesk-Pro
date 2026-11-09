using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Modules.AiAgent.Ingestion;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>Knowledge from a whole website, given only its domain (Issue #80, ADR 0030, SEC-05).</summary>
[Collection(AuthTests.Name)]
public sealed class SiteImportTests(AuthFixture fixture)
{
    private const string Path = "/api/v1/ai/sites";

    private static readonly string[] Attacker = ["https://attacker.example/"];

    private const string Paragraph = "Spediamo in tutta Italia con corriere tracciato e i tempi di consegna sono di due o tre giorni lavorativi per tutte le zone, isole comprese, ma per la Sardegna possono volerci alcuni giorni in più nei periodi di festa.";

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task With_a_sitemap_the_main_pages_are_ticked_and_shop_pages_and_forbidden_pages_are_left_out()
    {
        var (owner, _) = await OwnerAsync();
        var site = Domain();
        Api.Web.Page($"https://{site}/robots.txt", "User-agent: *\nDisallow: /segreto\n\nUser-agent: otherbot\nDisallow: /\n");
        Api.Web.Page($"https://{site}/", $"""<nav><a href="/chi-siamo">Chi siamo</a></nav><p>Benvenuti</p><a href="https://other-{site}/faq">FAQ altrove</a>""");
        Xml($"https://{site}/sitemap.xml", Urlset(site, "chi-siamo", "spedizioni", "pages/faq", "lookbook", "products/borsa-nera", "cart", "segreto/faq", "blog/2026/resi", "contatti?ref=1", "resi.pdf"));

        var found = await FindAsync(owner, site);

        Assert.Equal("ready", found.GetProperty("status").GetString());
        var pages = found.GetProperty("pages").EnumerateArray().ToList();
        Assert.Equal(
            [$"https://{site}/", $"https://{site}/chi-siamo", $"https://{site}/pages/faq", $"https://{site}/spedizioni"],
            pages.Where(p => p.GetProperty("chosen").GetBoolean()).Select(p => p.GetProperty("url").GetString()!).ToArray());
        // Found but not a main page: the owner can still tick it.
        Assert.Equal([$"https://{site}/lookbook"], pages.Where(p => !p.GetProperty("chosen").GetBoolean()).Select(p => p.GetProperty("url").GetString()!).ToArray());
        Assert.Equal("Chi siamo", pages[1].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Without_a_sitemap_links_lead_one_step_further_and_nothing_forbidden_or_elsewhere_is_fetched()
    {
        var (owner, _) = await OwnerAsync();
        var site = Domain();
        Api.Web.Page($"https://{site}/robots.txt", "User-agent: nihasdeskbot\nDisallow: /privato\nAllow: /privato/contatti\n");
        Api.Web.Page($"https://{site}/", $"""<footer><a href="/aiuto">Aiuto</a><a href="/privato/faq">FAQ privata</a><a href="/privato/contatti">Contatti</a><a href="https://elsewhere-{site}/resi">Resi</a></footer>""");
        Api.Web.Page($"https://{site}/aiuto", """<a href="/aiuto/spedizioni">Quanto costa spedire?</a><a href="/products/borsa">Borsa</a>""");

        var found = await FindAsync(owner, site);

        var chosen = found.GetProperty("pages").EnumerateArray().Where(p => p.GetProperty("chosen").GetBoolean()).Select(p => p.GetProperty("url").GetString()!).ToList();
        Assert.Contains($"https://{site}/aiuto", chosen);
        Assert.Contains($"https://{site}/aiuto/spedizioni", chosen);
        Assert.Contains($"https://{site}/privato/contatti", chosen);
        Assert.DoesNotContain(chosen, u => u.Contains("/privato/faq", StringComparison.Ordinal));
        Assert.DoesNotContain(Api.Web.Requested, u => u.Contains("/privato/faq", StringComparison.Ordinal) || u.Contains($"elsewhere-{site}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_site_whose_robots_txt_forbids_everything_is_left_alone()
    {
        var (owner, _) = await OwnerAsync();
        var site = Domain();
        Api.Web.Page($"https://{site}/robots.txt", "User-agent: *\nDisallow: /\n");
        Api.Web.Page($"https://{site}/", "<p>Home</p>");

        var found = await FindAsync(owner, site);

        Assert.Equal("failed", found.GetProperty("status").GetString());
        Assert.Equal("site_robots_forbid", found.GetProperty("failureReason").GetString());
        Assert.DoesNotContain($"https://{site}/", Api.Web.Requested);
    }

    [Fact]
    public async Task A_sitemap_with_a_dtd_is_not_processed_and_the_home_page_links_are_used()
    {
        var (owner, _) = await OwnerAsync();
        var site = Domain();
        Api.Web.Page($"https://{site}/", """<a href="/contatti">Contatti</a>""");
        Xml($"https://{site}/sitemap.xml", """<?xml version="1.0"?><!DOCTYPE urlset [<!ENTITY x SYSTEM "file:///etc/passwd">]><urlset><url><loc>&x;</loc></url></urlset>""");

        var found = await FindAsync(owner, site);

        Assert.Contains(found.GetProperty("pages").EnumerateArray(), p => p.GetProperty("url").GetString() == $"https://{site}/contatti");
    }

    [Fact]
    public async Task A_sitemap_index_is_followed_without_its_product_sitemaps()
    {
        var (owner, _) = await OwnerAsync();
        var site = Domain();
        Api.Web.Page($"https://{site}/robots.txt", $"Sitemap: https://{site}/sitemap.xml\nSitemap: https://elsewhere-{site}/sitemap.xml\n");
        Api.Web.Page($"https://{site}/", "<p>Home</p>");
        Xml($"https://{site}/sitemap.xml", $"""<sitemapindex><sitemap><loc>https://{site}/sitemap_products_1.xml</loc></sitemap><sitemap><loc>https://{site}/sitemap_pages_1.xml?from=1</loc></sitemap></sitemapindex>""");
        Xml($"https://{site}/sitemap_pages_1.xml?from=1", Urlset(site, "pages/contact-us"));

        var found = await FindAsync(owner, site);

        Assert.Contains(found.GetProperty("pages").EnumerateArray(), p => p.GetProperty("url").GetString() == $"https://{site}/pages/contact-us" && p.GetProperty("chosen").GetBoolean());
        Assert.DoesNotContain(Api.Web.Requested, u => u.Contains("sitemap_products", StringComparison.Ordinal) || u.Contains($"elsewhere-{site}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unreachable_site_is_tried_three_times_and_then_the_owner_is_told()
    {
        var (owner, _) = await OwnerAsync();
        var site = Domain();
        var id = await AddAsync(owner, site);

        // No page answers (404): robots.txt is fine without, the home page is not.
        for (var attempt = 0; attempt < SiteFinder.MaxAttempts; attempt++)
        {
            await Api.FindSitesAsync();
            Api.Clock.Advance(SiteFinder.LockFor + TimeSpan.FromMinutes(1));
        }

        var failed = await owner.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}");
        Assert.Equal("failed", failed.GetProperty("status").GetString());
        Assert.Equal("site_unreachable", failed.GetProperty("failureReason").GetString());
    }

    [Fact]
    public async Task Chosen_pages_become_web_page_sources_that_can_all_be_used_and_deleted_with_the_site()
    {
        var (owner, _) = await OwnerAsync();
        var site = Domain();
        Api.Web.Page($"https://{site}/", $"""<a href="/spedizioni">Spedizioni</a><main><h1>Benvenuti</h1><p>{Paragraph}</p></main>""");
        Api.Web.Page($"https://{site}/spedizioni", $"<main><h1>Spedizioni</h1><p>{Paragraph}</p></main>");
        var found = await FindAsync(owner, site);
        var id = found.GetProperty("id").GetString();

        // Only what the job found: a request can't make the system fetch another address.
        var other = await owner.PostAsync($"{Path}/{id}/pages", new { urls = Attacker });
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        Assert.DoesNotContain("https://attacker.example/", Api.Web.Requested);

        var urls = found.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("url").GetString()!).ToArray();
        var added = await (await owner.PostAsync($"{Path}/{id}/pages", new { urls })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, added.GetProperty("added").GetInt32());
        var again = await (await owner.PostAsync($"{Path}/{id}/pages", new { urls })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, again.GetProperty("added").GetInt32());
        Assert.Equal(2, again.GetProperty("alreadyAdded").GetInt32());

        await Api.ReadSourcesAsync();
        Assert.True((await owner.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}/differences")).GetProperty("count").GetInt32() >= 0);
        var used = await (await owner.PostAsync($"{Path}/{id}/use")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, used.GetProperty("used").GetInt32());
        var pages = (await owner.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}")).GetProperty("pages").EnumerateArray();
        Assert.All(pages, p => Assert.Equal("live", p.GetProperty("sourceStatus").GetString()));
        Assert.Equal(2, (await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/ai/sources")).GetArrayLength());

        await owner.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync($"{Path}/{id}")).StatusCode);
        Assert.Equal(0, (await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/ai/sources")).GetArrayLength());
    }

    [Fact]
    public async Task The_site_must_be_confirmed_public_and_new_and_other_workspaces_never_see_it()
    {
        var (owner, _) = await OwnerAsync();
        var site = Domain();

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync(Path, new { domain = site, confirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync(Path, new { domain = "not a domain", confirmed = true })).StatusCode);
        Assert.Equal("site.blocked", await CodeAsync(await owner.PostAsync(Path, new { domain = "127.0.0.1", confirmed = true })));

        var id = await AddAsync(owner, site);
        Assert.Equal("site.duplicate", await CodeAsync(await owner.PostAsync(Path, new { domain = $"https://www.{site}/some/page", confirmed = true })));

        var (stranger, _) = await OwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"{Path}/{id}")).StatusCode);
        Assert.Equal(0, (await stranger.Http.GetFromJsonAsync<JsonElement>(Path)).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"{Path}/{id}/use")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"{Path}/{id}/differences")).StatusCode);
    }

    [Theory]
    [InlineData("example.com", "example.com", "https://example.com/")]
    [InlineData("  https://www.Example.com/about?x=1 ", "example.com", "https://www.example.com/")]
    [InlineData("http://shop.example.it", "shop.example.it", "http://shop.example.it/")]
    public void A_domain_means_the_site_and_nothing_else_from_the_text(string text, string host, string root)
    {
        Assert.True(SiteAddress.TryParse(text, out var parsedHost, out var parsedRoot));
        Assert.Equal(host, parsedHost);
        Assert.Equal(root, parsedRoot.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("example")]
    [InlineData("ftp://example.com")]
    [InlineData("https://example.com:8443")]
    [InlineData("https://user:secret@example.com")]
    public void Other_text_is_not_a_domain(string text) =>
        Assert.False(SiteAddress.TryParse(text, out _, out _));

    [Theory]
    [InlineData("/x", true)]
    [InlineData("/segreto", false)]
    [InlineData("/segreto/pagina", false)]
    [InlineData("/segreto/ok", true)]
    [InlineData("/doc.pdf", false)]
    [InlineData("/doc.pdf/altro", true)]
    public void Robots_rules_follow_the_longest_match(string path, bool allowed)
    {
        var rules = RobotsRules.Parse("User-agent: *\nDisallow: /segreto # note\nAllow: /segreto/ok\nDisallow: /*.pdf$\n");
        Assert.Equal(allowed, rules.Allows(path));
    }

    private async Task<JsonElement> FindAsync(ApiClient owner, string site)
    {
        var id = await AddAsync(owner, site);
        await Api.FindSitesAsync();
        return await owner.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}");
    }

    private static async Task<string> AddAsync(ApiClient owner, string site)
    {
        var added = await owner.PostAsync(Path, new { domain = site, confirmed = true });
        added.EnsureSuccessStatusCode();
        return (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private void Xml(string url, string xml) => Api.Web.Pages[url] = ("application/xml", Encoding.UTF8.GetBytes(xml));

    private static string Urlset(string site, params string[] paths) =>
        $"""<?xml version="1.0"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">{string.Concat(paths.Select(p => $"<url><loc>https://{site}/{p}</loc></url>"))}</urlset>""";

    private static string Domain() => $"shop-{Guid.NewGuid():N}.example";

    private async Task<(ApiClient Client, string Workspace)> OwnerAsync()
    {
        var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), "Almanaam")).Content.ReadFromJsonAsync<JsonElement>();
        return (client, me.GetProperty("workspace").GetProperty("id").GetString()!);
    }
}
