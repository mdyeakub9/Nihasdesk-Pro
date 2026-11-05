using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Ingestion;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>From file or page to searchable sections, and every plain failure (PRD 4.7, ADR 0026 §2, SEC-04, SEC-05).</summary>
[Collection(AuthTests.Name)]
public sealed class SourceReadingTests(AuthFixture fixture)
{
    private const string Path = "/api/v1/ai/sources";

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task An_italian_pdf_becomes_sections_with_headings_and_pages()
    {
        var (owner, tenant) = await OwnerAsync();

        var id = await UploadAsync(owner, SourceFixtures.Pdf(), "guida.pdf");
        await Api.ReadSourcesAsync();

        var source = await SourceAsync(owner, id);
        Assert.Equal("review", source.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, source.GetProperty("failureReason").ValueKind);

        var sections = await SectionsAsync(tenant, id);
        var shipping = sections.Single(s => s.HeadingPath == "Spedizioni");
        Assert.Equal(1, shipping.Page);
        Assert.Contains("corriere tracciato", shipping.Text, StringComparison.Ordinal);
        Assert.Contains("quattro giorni", shipping.Text, StringComparison.Ordinal);
        Assert.Contains(sections, s => s.HeadingPath == "Resi" && s.Page == 1 && s.Text.Contains("quattordici", StringComparison.Ordinal));
        Assert.Contains(sections, s => s.HeadingPath == "Resi" && s.Page == 2 && s.Text.Contains("etichetta", StringComparison.Ordinal));
        Assert.All(sections, s =>
        {
            Assert.Equal("it", s.Language);
            Assert.Equal("fake-embedding", s.EmbeddingModel);
            Assert.NotNull(s.TextHash);
        });

        await AssertSearchableAsync(tenant, id, "spedizion");
    }

    [Fact]
    public void A_real_shaped_pdf_has_no_running_headers_and_keeps_wrapped_lines_and_questions_together()
    {
        var document = new PdfParser().Parse(SourceFixtures.RealisticPdf(), "guida.pdf");
        var blocks = document.Sections.SelectMany(s => s.Blocks.Select(b => b.Text)).ToList();
        var all = string.Join(" ", blocks);

        Assert.DoesNotContain("NEGOZIO DEMO", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Documento interno", all, StringComparison.Ordinal);
        Assert.Contains("A: Non ancora. Spediamo solo agli indirizzi in Italia, secondo la copertura del corriere e la disponibilita.", blocks);

        // A sentence cut by the page break goes on, and the page number stays where it started.
        var chunks = Chunker.Split(document.Sections);
        Assert.Contains(chunks, c => c.Page == 2 && c.Text.Contains("dalla zona e dal periodo dell'anno.", StringComparison.Ordinal));
        Assert.Equal(1, chunks.Single(c => c.Text.Contains("Spedite fuori", StringComparison.Ordinal)).Page);

        // A question at the bottom of a page and its answer on the next are one FAQ pair.
        var pair = chunks.Single(c => c.Text.Contains("negozio fisico", StringComparison.Ordinal));
        Assert.Equal(SectionKind.Faq, pair.Kind);
        Assert.Contains("A: No, solo online.", pair.Text, StringComparison.Ordinal);
        Assert.Equal(SectionKind.Faq, chunks.Single(c => c.Text.Contains("Spedite fuori", StringComparison.Ordinal)).Kind);

        Assert.Equal(3, chunks.First(c => c.HeadingPath == "Resi").Page);

        // Columns make a table with its rows, and the text after it stays text.
        var table = chunks.Single(c => c.Kind == SectionKind.Table);
        Assert.Equal("Consegna | Costo\nStandard | 5,90 euro\nCorriere rapido | 9,90 euro", table.Text);
        Assert.Contains(chunks, c => c.Kind == SectionKind.Text && c.Text.Contains("dal lunedi al venerdi", StringComparison.Ordinal));
        Assert.Empty(document.PagesWithoutText);
        Assert.All(chunks, c => Assert.True(c.Tokens > 3, c.Text));
    }

    [Fact]
    public void Short_text_takes_the_documents_language_and_one_foreign_word_does_not_change_it()
    {
        Assert.Equal("en", LanguageGuess.Of("Delivery", "en"));
        Assert.Equal("it", LanguageGuess.Of("Il reso è gratuito entro due giorni per la zona", "en"));
        Assert.Equal("en", LanguageGuess.Of("The price of la dolce vita is free for you", "it"));
        Assert.Null(LanguageGuess.Of("Ciao", null));
    }

    [Fact]
    public async Task An_italian_docx_keeps_heading_paths_lists_and_whole_tables()
    {
        var (owner, tenant) = await OwnerAsync();

        var id = await UploadAsync(owner, SourceFixtures.Docx(), "guida.docx");
        await Api.ReadSourcesAsync();

        Assert.Equal("review", (await SourceAsync(owner, id)).GetProperty("status").GetString());
        var sections = await SectionsAsync(tenant, id);
        Assert.Contains(sections, s => s.HeadingPath == "Pagamenti" && s.Text.Contains("contanti", StringComparison.Ordinal));

        var list = sections.Single(s => s.Kind == SectionKind.List);
        Assert.Equal("Pagamenti › Metodi", list.HeadingPath);
        Assert.Equal("- Contrassegno\n- Bonifico per le aziende", list.Text);

        var table = sections.Single(s => s.Kind == SectionKind.Table);
        Assert.Equal("Zone di consegna", table.HeadingPath);
        Assert.Equal("Zona | Costo\nSardegna | 5 euro\nItalia | 7,90 euro", table.Text);
    }

    [Fact]
    public async Task A_web_page_keeps_its_main_content_and_drops_menus_footers_and_scripts()
    {
        var (owner, tenant) = await OwnerAsync();
        var url = $"https://93.184.216.34/{Guid.NewGuid():N}";
        Api.Web.Page(url, SourceFixtures.ItalianPage);

        var id = await AddPageAsync(owner, url);
        await Api.ReadSourcesAsync();

        var source = await SourceAsync(owner, id);
        Assert.Equal("review", source.GetProperty("status").GetString());
        Assert.Contains("Spedizioni e resi", source.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal(url, source.GetProperty("origin").GetString());

        var sections = await SectionsAsync(tenant, id);
        Assert.Contains(sections, s => s.HeadingPath!.EndsWith("Isole", StringComparison.Ordinal) && s.Text.Contains("Sardegna", StringComparison.Ordinal));
        Assert.Contains(sections, s => s.Kind == SectionKind.Table && s.Text.Contains("Italia | 2-4", StringComparison.Ordinal));
        var all = string.Join(' ', sections.Select(s => s.Text));
        Assert.DoesNotContain("Prodotti", all, StringComparison.Ordinal);
        Assert.DoesNotContain("diritti riservati", all, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", all, StringComparison.Ordinal);
        Assert.DoesNotContain("cookie", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_long_page_without_main_or_article_keeps_its_table_and_a_latin1_page_is_decoded()
    {
        var (owner, tenant) = await OwnerAsync();
        var url = $"https://93.184.216.34/{Guid.NewGuid():N}";
        var filler = string.Concat(Enumerable.Repeat("<p>Il nostro laboratorio lavora la pelle a mano da molti anni, con cura e con materiali scelti uno per uno, e può spedire in tutta Italia.</p>", 12));
        var html = SourceFixtures.ItalianPage
            .Replace("<article>", $"<div class=\"content\">{filler}", StringComparison.Ordinal)
            .Replace("</article>", "</div>", StringComparison.Ordinal)
            .Replace("<head>", "<head><meta charset=\"iso-8859-1\">", StringComparison.Ordinal);
        Api.Web.Pages[url] = ("text/html", Encoding.Latin1.GetBytes(html));

        var id = await AddPageAsync(owner, url);
        await Api.ReadSourcesAsync();

        Assert.Equal("review", (await SourceAsync(owner, id)).GetProperty("status").GetString());
        var sections = await SectionsAsync(tenant, id);
        Assert.Contains(sections, s => s.Kind == SectionKind.Table && s.Text.Contains("Italia | 2-4", StringComparison.Ordinal));
        var all = string.Join(' ', sections.Select(s => s.Text));
        Assert.Contains("può spedire", all, StringComparison.Ordinal);
        Assert.DoesNotContain("diritti riservati", all, StringComparison.Ordinal);
        Assert.DoesNotContain("cookie", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Plain_text_is_split_into_paragraphs()
    {
        var (owner, tenant) = await OwnerAsync();

        var id = await UploadAsync(owner, Encoding.UTF8.GetBytes("Orari di apertura?\nLun–Sab 9–19.\n\nSiamo chiusi la domenica."), "orari.txt");
        await Api.ReadSourcesAsync();

        Assert.Equal("review", (await SourceAsync(owner, id)).GetProperty("status").GetString());
        Assert.Contains("Lun–Sab", (await SectionsAsync(tenant, id))[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_scanned_pdf_fails_with_a_plain_reason()
    {
        var (owner, tenant) = await OwnerAsync();

        var id = await UploadAsync(owner, SourceFixtures.ScannedPdf(), "scansione.pdf");
        await Api.ReadSourcesAsync();

        await AssertFailedAsync(owner, id, FailureCodes.Scanned);
        Assert.Empty(await SectionsAsync(tenant, id));
    }

    [Fact]
    public async Task Pages_without_text_are_reported_and_the_rest_is_read()
    {
        var (owner, tenant) = await OwnerAsync();

        var id = await UploadAsync(owner, SourceFixtures.Pdf(emptyPages: 2), "misto.pdf");
        await Api.ReadSourcesAsync();

        var source = await SourceAsync(owner, id);
        Assert.Equal("review", source.GetProperty("status").GetString());
        Assert.Equal("pages_without_text:3,4", Assert.Single(source.GetProperty("warnings").EnumerateArray()).GetString());
        Assert.Equal(4, source.GetProperty("pageCount").GetInt32());
        Assert.NotEmpty(await SectionsAsync(tenant, id));
    }

    [Fact]
    public async Task An_encrypted_pdf_fails_with_a_plain_reason()
    {
        var (owner, _) = await OwnerAsync();

        var id = await UploadAsync(owner, SourceFixtures.EncryptedPdf(), "protetto.pdf");
        await Api.ReadSourcesAsync();

        await AssertFailedAsync(owner, id, FailureCodes.Encrypted);
    }

    [Fact]
    public async Task A_pdf_over_300_pages_or_a_broken_one_fails_with_a_plain_reason()
    {
        var (owner, _) = await OwnerAsync();

        var tooLong = await UploadAsync(owner, SourceFixtures.LongPdf(PdfParser.MaxPages + 1), "lungo.pdf");
        var broken = await UploadAsync(owner, Encoding.ASCII.GetBytes("%PDF-1.7\nnon è un pdf"), "rotto.pdf");
        await Api.ReadSourcesAsync();

        await AssertFailedAsync(owner, tooLong, FailureCodes.TooManyPages);
        await AssertFailedAsync(owner, broken, FailureCodes.Unreadable);
    }

    [Fact]
    public async Task A_page_that_redirects_to_a_private_address_fails_and_is_not_fetched()
    {
        var (owner, _) = await OwnerAsync();
        var url = $"https://93.184.216.34/{Guid.NewGuid():N}";
        Api.Web.Pages[url] = ("text/html", []);
        Api.Web.Redirects[url] = "http://169.254.169.254/latest/meta-data/";

        var id = await AddPageAsync(owner, url);
        await Api.ReadSourcesAsync();

        await AssertFailedAsync(owner, id, FailureCodes.BlockedUrl);
        Assert.DoesNotContain(Api.Web.Requested, r => r.Contains("169.254", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_page_that_is_not_html_or_too_large_fails()
    {
        var (owner, _) = await OwnerAsync();
        var pdf = $"https://93.184.216.34/{Guid.NewGuid():N}.pdf";
        var huge = $"https://93.184.216.34/{Guid.NewGuid():N}";
        Api.Web.Pages[pdf] = ("application/pdf", SourceFixtures.Pdf());
        Api.Web.Pages[huge] = ("text/html", new byte[SourceReader.MaxPageBytes + 1]);

        var notPage = await AddPageAsync(owner, pdf);
        var tooLarge = await AddPageAsync(owner, huge);
        await Api.ReadSourcesAsync();

        await AssertFailedAsync(owner, notPage, FailureCodes.NotAPage);
        await AssertFailedAsync(owner, tooLarge, FailureCodes.PageTooLarge);
    }

    [Fact]
    public async Task A_site_that_is_down_is_tried_three_times_then_fails()
    {
        var (owner, _) = await OwnerAsync();
        var url = $"https://93.184.216.34/{Guid.NewGuid():N}";

        var id = await AddPageAsync(owner, url);
        for (var attempt = 1; attempt < SourceReader.MaxAttempts; attempt++)
        {
            await Api.ReadSourcesAsync();
            Assert.Equal("reading", (await SourceAsync(owner, id)).GetProperty("status").GetString());
            Api.Clock.Advance(TimeSpan.FromMinutes(attempt + 1));
        }

        await Api.ReadSourcesAsync();
        await AssertFailedAsync(owner, id, FailureCodes.PageUnavailable);
    }

    [Fact]
    public async Task When_the_embedding_provider_stays_down_the_source_fails_and_leaves_no_sections()
    {
        var (owner, tenant) = await OwnerAsync();
        Api.Embeddings.FailWith = "http_500";
        try
        {
            var id = await UploadAsync(owner, SourceFixtures.Pdf(emptyPages: 1), "provider.pdf");
            for (var attempt = 1; attempt < SourceReader.MaxAttempts; attempt++)
            {
                await Api.ReadSourcesAsync();
                Api.Clock.Advance(TimeSpan.FromMinutes(attempt + 1));
            }

            await Api.ReadSourcesAsync();

            await AssertFailedAsync(owner, id, FailureCodes.EmbeddingFailed);
            Assert.Empty(await SectionsAsync(tenant, id));
        }
        finally
        {
            Api.Embeddings.FailWith = null;
        }
    }

    [Fact]
    public async Task Without_an_ai_key_sources_are_still_read_and_the_owner_is_told()
    {
        var (owner, tenant) = await OwnerAsync();
        Api.Embeddings.IsConfigured = false;
        try
        {
            var id = await UploadAsync(owner, SourceFixtures.Docx(), "senza-chiave.docx");
            await Api.ReadSourcesAsync();

            Assert.Equal("review", (await SourceAsync(owner, id)).GetProperty("status").GetString());
            await using var scope = Scope(tenant);
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            Assert.Equal(["no_embeddings"], (await db.Sources.SingleAsync(s => s.Id == id)).Warnings);
            Assert.All(await db.Sections.Where(s => s.SourceId == id).ToListAsync(), s => Assert.Null(s.EmbeddingModel));
            await AssertSearchableAsync(tenant, id, "contant");
        }
        finally
        {
            Api.Embeddings.IsConfigured = true;
        }
    }

    [Fact]
    public async Task The_same_text_is_embedded_once()
    {
        var (owner, tenant) = await OwnerAsync();
        var text = "Consegna rapida " + Guid.NewGuid().ToString("N");

        // Same title (file name) and text in two files that differ only by a line ending.
        var first = await UploadAsync(owner, Encoding.UTF8.GetBytes(text + "\n"), "uguale.txt");
        await Api.ReadSourcesAsync();
        var second = await UploadAsync(owner, Encoding.UTF8.GetBytes(text + "\n\n"), "uguale.txt");
        await Api.ReadSourcesAsync();

        Assert.Single(Api.Embeddings.Embedded, e => e.Contains(text, StringComparison.Ordinal));
        Assert.All(await SectionsAsync(tenant, second), s => Assert.Equal("fake-embedding", s.EmbeddingModel));
        await using var scope = Scope(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>().Database;
        Assert.Equal(1, await db.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM ai.knowledge_sections WHERE source_id = {second} AND embedding IS NOT NULL").SingleAsync());
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Long_text_is_split_with_overlap_and_tables_stay_whole()
    {
        var sentence = "Questa frase parla delle consegne del negozio e di come funzionano.";
        var longText = string.Join(' ', Enumerable.Repeat(sentence, 200));
        var table = string.Join('\n', Enumerable.Range(0, 300).Select(i => $"Zona {i} | {i} euro | due giorni"));

        var chunks = Chunker.Split([new DocumentSection(["Spedizioni", "Italia"], 3,
            [new DocumentBlock(SectionKind.Text, longText), new DocumentBlock(SectionKind.Table, table)])]);

        Assert.All(chunks, c =>
        {
            Assert.Equal("Spedizioni › Italia", c.HeadingPath);
            Assert.Equal(3, c.Page);
        });
        var text = chunks.Where(c => c.Kind == SectionKind.Text).ToList();
        Assert.True(text.Count > 1);
        Assert.All(text, c => Assert.InRange(c.Tokens, 1, Chunker.TargetTokens));
        // The start of each next part repeats the end of the last one.
        Assert.StartsWith(sentence, text[1].Text, StringComparison.Ordinal);
        Assert.EndsWith(sentence, text[0].Text, StringComparison.Ordinal);
        Assert.Equal(table, Assert.Single(chunks, c => c.Kind == SectionKind.Table).Text);
    }

    [Fact]
    public void A_question_stays_with_its_answer_and_nothing_is_lost()
    {
        var filler = string.Join(' ', Enumerable.Repeat("Parole di riempimento del documento.", 50));

        var chunks = Chunker.Split([new DocumentSection([], null,
            [
                new DocumentBlock(SectionKind.Text, filler),
                new DocumentBlock(SectionKind.Text, "Fate spedizioni all'estero?"),
                new DocumentBlock(SectionKind.Text, "Sì, nell'Unione Europea."),
            ])]);

        Assert.Contains(chunks, c => c.Text.Contains("Fate spedizioni all'estero?\nSì, nell'Unione Europea.", StringComparison.Ordinal));
        Assert.Null(chunks[0].HeadingPath);
    }

    private async Task AssertSearchableAsync(Guid tenant, Guid id, string word)
    {
        await using var scope = Scope(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>().Database;
        var query = word + ":*";
        Assert.True(await db.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM ai.knowledge_sections WHERE source_id = {id} AND search_vector @@ to_tsquery('simple', {query})").SingleAsync() > 0);
    }

    private static async Task AssertFailedAsync(ApiClient owner, Guid id, string code)
    {
        var source = await SourceAsync(owner, id);
        Assert.Equal("failed", source.GetProperty("status").GetString());
        Assert.Equal(code, source.GetProperty("failureReason").GetString());
    }

    private static async Task<JsonElement> SourceAsync(ApiClient owner, Guid id) =>
        (await owner.Http.GetFromJsonAsync<JsonElement>(Path)).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == id);

    private async Task<List<KnowledgeSection>> SectionsAsync(Guid tenant, Guid id)
    {
        await using var scope = Scope(tenant);
        return await scope.ServiceProvider.GetRequiredService<AiDbContext>().Sections.AsNoTracking()
            .Where(s => s.SourceId == id).OrderBy(s => s.Position).ToListAsync();
    }

    private AsyncServiceScope Scope(Guid tenant)
    {
        var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenant);
        return scope;
    }

    private static async Task<Guid> UploadAsync(ApiClient client, byte[] content, string fileName)
    {
        await client.RefreshCsrfAsync();
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        var response = await client.Http.PostAsync($"{Path}/files", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddPageAsync(ApiClient client, string url)
    {
        var response = await client.PostAsync($"{Path}/web", new { url });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<(ApiClient Client, Guid Tenant)> OwnerAsync()
    {
        var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), "Bottega Eval")).Content.ReadFromJsonAsync<JsonElement>();
        return (client, me.GetProperty("workspace").GetProperty("id").GetGuid());
    }
}
