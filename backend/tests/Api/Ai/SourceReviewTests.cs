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
using Nihasdesk.Modules.Shipping.Contracts;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>Knowledge review: what the AI will find, edits, and conflicts with the settings (PRD 4.7, SEC-06, SEC-10).</summary>
[Collection(AuthTests.Name)]
public sealed class SourceReviewTests(AuthFixture fixture)
{
    private const string Path = "/api/v1/ai/sources";

    private static readonly ShippingRate Italia = new(Guid.NewGuid(), "Italia", null, 490, "EUR", 9900, 2, 4);
    private static readonly ShippingRate Sardegna = new(Guid.NewGuid(), "Sardegna", null, 790, "EUR", null, 3, 5);

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task A_document_that_differs_from_the_settings_shows_conflicts_and_fixing_the_setting_clears_them()
    {
        var (owner, _) = await OwnerAsync();
        await PutAsync(owner, "/api/v1/shipping/zones", new { zones = new object[] { new { name = "Italia", price = 490, currency = "EUR", freeOver = 9900, minDays = 2, maxDays = 4 } } });
        await PutAsync(owner, "/api/v1/ai/policies", new { returnDays = 14 });
        var id = await ReadTextAsync(owner, "La spedizione costa 5 € in tutta Italia.\n\nIl reso è possibile entro 30 giorni dalla consegna.\n\nAccettiamo PayPal e bonifico.");

        var review = await ReviewAsync(owner, id);

        var conflicts = review.GetProperty("conflicts").EnumerateArray().ToList();
        var shipping = conflicts.Single(c => c.GetProperty("kind").GetString() == "shipping_cost");
        Assert.Equal("5.00 EUR", shipping.GetProperty("found").GetString());
        Assert.Equal("Italia: 4.90 EUR / 99.00 EUR", shipping.GetProperty("expected").GetString());
        var returns = conflicts.Single(c => c.GetProperty("kind").GetString() == "return_days");
        Assert.Equal("30 giorni", returns.GetProperty("found").GetString());
        Assert.Equal("14", returns.GetProperty("expected").GetString());
        Assert.Equal(2, conflicts.Count(c => c.GetProperty("kind").GetString() == "payment_method"));

        await PutAsync(owner, "/api/v1/shipping/zones", new { zones = new object[] { new { name = "Italia", price = 500, currency = "EUR", minDays = 2, maxDays = 4 } } });
        await PutAsync(owner, "/api/v1/ai/policies", new { returnDays = 30 });
        var after = (await ReviewAsync(owner, id)).GetProperty("conflicts").EnumerateArray().ToList();
        Assert.DoesNotContain(after, c => c.GetProperty("kind").GetString() is "shipping_cost" or "return_days");
    }

    [Theory]
    [InlineData("La spedizione in Italia costa 4,90 €.", null)]
    [InlineData("Spedizione gratuita da 99 euro.", null)]
    [InlineData("La spedizione in Sardegna costa 7,90 €.", null)]
    [InlineData("La spedizione in Sardegna costa 4,90 €.", "shipping_cost")]
    [InlineData("Shipping is €5.00.", "shipping_cost")]
    [InlineData("La borsa costa 89 €.", null)]
    [InlineData("Return shipping costs €5.", null)]
    [InlineData("La consegna richiede da 2 a 4 giorni.", null)]
    [InlineData("Delivery takes 10 business days.", "delivery_days")]
    [InlineData("La consegna in Sardegna richiede 5 giorni.", null)]
    [InlineData("La consegna in Italia richiede 5 giorni.", "delivery_days")]
    [InlineData("Il reso è possibile entro 14 giorni.", null)]
    [InlineData("Returns are accepted within 30 days.", "return_days")]
    [InlineData("The refund for returns arrives within 7 days.", null)]
    [InlineData("Non accettiamo PayPal.", null)]
    [InlineData("We accept Visa and Mastercard.", "payment_method")]
    [InlineData("Paghi con carta di credito.", "payment_method")]
    [InlineData("La carta regalo non scade.", null)]
    public void Conflicts_are_found_by_what_the_sentence_is_about(string text, string? kind)
    {
        var section = KnowledgeSection.Create(Guid.NewGuid(), Guid.NewGuid(), 0, null, 1, SectionKind.Text, text, 10, "it");

        var conflicts = ConflictCheck.Find([section], new BusinessFacts([Italia, Sardegna], 14));

        if (kind is null)
        {
            Assert.Empty(conflicts);
        }
        else
        {
            Assert.Contains(conflicts, c => c.Kind == kind && c.SectionId == section.Id);
        }
    }

    [Fact]
    public void A_table_row_is_about_what_its_heading_says_and_empty_settings_or_excluded_sections_give_no_conflict()
    {
        var tenant = Guid.NewGuid();
        var row = KnowledgeSection.Create(tenant, Guid.NewGuid(), 0, "Doc › Shipping and delivery", 4, SectionKind.Table, "Delivery option | Cost\nStandard | €5.90", 10, "en");

        var conflict = Assert.Single(ConflictCheck.Find([row], new BusinessFacts([Italia], null)));
        Assert.Equal("shipping_cost", conflict.Kind);
        Assert.Equal(4, conflict.Page);
        Assert.Equal("5.90 EUR", conflict.Found);

        Assert.Empty(ConflictCheck.Find([row], new BusinessFacts([], null)));

        row.SetExcluded(true);
        Assert.Empty(ConflictCheck.Find([row], new BusinessFacts([Italia], null)));
    }

    [Fact]
    public async Task A_price_list_is_pointed_out()
    {
        var (owner, _) = await OwnerAsync();
        var id = await ReadTextAsync(owner, "Listino: borsa 89 €, zaino 129 €, cintura 35 €, sciarpa 35 €, portachiavi 12 €.\n\nSiamo a Sassari.");

        var review = await ReviewAsync(owner, id);

        Assert.Single(review.GetProperty("priceListSections").EnumerateArray());
    }

    [Fact]
    public async Task Sections_are_listed_in_pages_and_members_read_but_do_not_change()
    {
        var (owner, tenant) = await OwnerAsync();
        var member = await MemberOfAsync(owner);
        var id = await ReadTextAsync(owner, "Primo paragrafo con il testo del negozio.");
        await using (var scope = Scope(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            db.Sections.AddRange(Enumerable.Range(10, 150).Select(i =>
                KnowledgeSection.Create(tenant, id, i, "Titolo", 1, SectionKind.Text, $"Sezione {i}", 2, "it")));
            await db.SaveChangesAsync();
        }

        var first = await member.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}/sections");
        Assert.Equal(100, first.GetProperty("items").GetArrayLength());
        var after = first.GetProperty("nextAfter").GetInt32();
        var second = await member.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}/sections?after={after}");
        Assert.Equal(51, second.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextAfter").ValueKind);

        var section = first.GetProperty("items")[0].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(member, $"{Path}/{id}/sections/{section}", new { text = "Intruso" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"{Path}/{id}/sections/{section}/exclude")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"{Path}/{id}/sections/{section}/merge-next")).StatusCode);
    }

    [Fact]
    public async Task Editing_replaces_the_search_data_and_a_provider_failure_keeps_the_text()
    {
        var (owner, tenant) = await OwnerAsync();
        var id = await ReadTextAsync(owner, "Testo sbagliato del negozio con parole vecchie.");
        var section = await FirstSectionAsync(owner, id);

        var marker = "elefante" + Guid.NewGuid().ToString("N")[..6];
        var edited = await (await PutAsync(owner, $"{Path}/{id}/sections/{section}", new { text = $"  La nuova parola è {marker}. " })).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal($"La nuova parola è {marker}.", edited.GetProperty("text").GetString());
        Assert.True(edited.GetProperty("searchable").GetBoolean());
        Assert.Contains(Api.Embeddings.Embedded, e => e.Contains(marker, StringComparison.Ordinal));
        Assert.Equal(1, await CountAsync(tenant, section, marker));
        Assert.Equal(0, await CountAsync(tenant, section, "vecchie"));

        Api.Embeddings.FailWith = "http_500";
        try
        {
            var kept = await PutAsync(owner, $"{Path}/{id}/sections/{section}", new { text = "Testo salvato senza vettore." });
            Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
            Assert.False((await kept.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("searchable").GetBoolean());
        }
        finally
        {
            Api.Embeddings.FailWith = null;
        }

        await using var scope = Scope(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>().Database;
        Assert.Equal(1, await db.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM ai.knowledge_sections WHERE id = {section} AND embedding IS NULL AND embedding_model IS NULL").SingleAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(owner, $"{Path}/{id}/sections/{section}", new { text = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(owner, $"{Path}/{id}/sections/{section}", new { text = new string('x', KnowledgeSection.TextMaxLength + 1) })).StatusCode);
    }

    [Fact]
    public async Task Excluding_takes_a_section_out_of_the_conflict_check_and_can_be_undone()
    {
        var (owner, _) = await OwnerAsync();
        await PutAsync(owner, "/api/v1/shipping/zones", new { zones = new object[] { new { name = "Italia", price = 490, currency = "EUR" } } });
        var id = await ReadTextAsync(owner, "La spedizione costa 5 € in tutta Italia.");
        var section = await FirstSectionAsync(owner, id);
        Assert.Single((await ReviewAsync(owner, id)).GetProperty("conflicts").EnumerateArray());

        var excluded = await (await owner.PostAsync($"{Path}/{id}/sections/{section}/exclude")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(excluded.GetProperty("excluded").GetBoolean());
        Assert.Empty((await ReviewAsync(owner, id)).GetProperty("conflicts").EnumerateArray());

        await owner.PostAsync($"{Path}/{id}/sections/{section}/include");
        Assert.Single((await ReviewAsync(owner, id)).GetProperty("conflicts").EnumerateArray());
    }

    [Fact]
    public async Task Merging_joins_a_section_with_the_next_and_the_last_has_no_next()
    {
        var (owner, tenant) = await OwnerAsync();
        var id = await ReadTextAsync(owner, "Primo paragrafo della guida.");
        var first = await FirstSectionAsync(owner, id);
        Guid second;
        await using (var scope = Scope(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            var added = KnowledgeSection.Create(tenant, id, 5, null, 1, SectionKind.Table, "Zona | Giorni", 3, "it");
            db.Sections.Add(added);
            await db.SaveChangesAsync();
            second = added.Id;
        }

        var merged = await (await owner.PostAsync($"{Path}/{id}/sections/{first}/merge-next")).Content.ReadFromJsonAsync<JsonElement>();

        var text = merged.GetProperty("text").GetString()!;
        Assert.StartsWith("Primo paragrafo della guida.", text, StringComparison.Ordinal);
        Assert.EndsWith("\n\nZona | Giorni", text, StringComparison.Ordinal);
        Assert.Equal("text", merged.GetProperty("kind").GetString());
        var items = (await owner.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}/sections")).GetProperty("items");
        Assert.DoesNotContain(items.EnumerateArray(), s => s.GetProperty("id").GetGuid() == second);
        Assert.Equal("source.no_next_section", await AuthFlowTests.CodeOf(await owner.PostAsync($"{Path}/{id}/sections/{first}/merge-next")));
    }

    [Fact]
    public async Task Another_workspace_finds_nothing_and_a_section_must_belong_to_the_source()
    {
        var (owner, _) = await OwnerAsync();
        var (other, _) = await OwnerAsync();
        var id = await ReadTextAsync(owner, "Testo riservato del negozio.");
        var otherId = await ReadTextAsync(owner, "Altro documento del negozio.");
        var section = await FirstSectionAsync(owner, id);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.GetAsync($"{Path}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.GetAsync($"{Path}/{id}/sections")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(other, $"{Path}/{id}/sections/{section}", new { text = "Intruso" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"{Path}/{id}/sections/{section}/exclude")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"{Path}/{id}/sections/{section}/merge-next")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(owner, $"{Path}/{otherId}/sections/{section}", new { text = "Fonte sbagliata" })).StatusCode);
    }

    private async Task<int> CountAsync(Guid tenant, Guid section, string word)
    {
        await using var scope = Scope(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>().Database;
        return await db.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM ai.knowledge_sections WHERE id = {section} AND search_vector @@ to_tsquery('simple', {word + ":*"})").SingleAsync();
    }

    private static async Task<JsonElement> ReviewAsync(ApiClient client, Guid id) =>
        await client.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}");

    private static async Task<Guid> FirstSectionAsync(ApiClient client, Guid id) =>
        (await client.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}/sections")).GetProperty("items")[0].GetProperty("id").GetGuid();

    /// <summary>Uploads a text file and reads it, like the Workers job.</summary>
    private async Task<Guid> ReadTextAsync(ApiClient client, string text)
    {
        await client.RefreshCsrfAsync();
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text + " " + Guid.NewGuid().ToString("N")[..4]));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "guida.txt");
        var response = await client.Http.PostAsync($"{Path}/files", form);
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Api.ReadSourcesAsync();
        return id;
    }

    private AsyncServiceScope Scope(Guid tenant)
    {
        var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenant);
        return scope;
    }

    private static async Task<HttpResponseMessage> PutAsync(ApiClient client, string path, object body)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PutAsJsonAsync(path, body);
    }

    private async Task<(ApiClient Client, Guid Tenant)> OwnerAsync(string business = "Bottega Eval")
    {
        var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), business)).Content.ReadFromJsonAsync<JsonElement>();
        return (client, me.GetProperty("workspace").GetProperty("id").GetGuid());
    }

    private async Task<ApiClient> MemberOfAsync(ApiClient owner)
    {
        var email = AuthFixture.NewEmail();
        await owner.PostAsync("/api/v1/team/invitations", new { email, role = "member" });
        var link = System.Text.RegularExpressions.Regex.Match(Api.Emails.LastTo(email).TextBody, @"https://\S+accept-invitation\?token=\S+").Value;
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)["token"];
        var member = Api.CreateApiClient();
        (await member.PostAsync("/api/v1/invitations/accept", new { token, name = "Luca", password = "girasole tramonto barca verde" })).EnsureSuccessStatusCode();
        return member;
    }
}
