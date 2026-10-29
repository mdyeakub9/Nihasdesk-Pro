using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>Shop policies, shipping zones and FAQ, and the AI answering from them (PRD 4.7, ADR 0020).</summary>
[Collection(AuthTests.Name)]
public sealed class KnowledgeBaseTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    private static readonly object[] Zones =
    [
        new { name = "Italia", areas = "Tutta Italia tranne isole", price = 590, currency = "EUR", freeOver = 5000, minDays = 2, maxDays = 4 },
        new { name = "Sardegna e isole", price = 990, currency = "EUR", freeOver = 8000, minDays = 3, maxDays = 6 },
    ];

    [Fact]
    public async Task Owners_write_policies_members_read_them_and_other_workspaces_see_nothing()
    {
        var owner = await OwnerAsync();
        var empty = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/ai/policies");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("returnPolicy").ValueKind);
        Assert.Equal(["cash_on_delivery"], empty.GetProperty("paymentMethods").EnumerateArray().Select(m => m.GetString()));

        var saved = await PutAsync(owner, "/api/v1/ai/policies", new
        {
            returnDays = 14,
            returnPolicy = "Reso gratuito entro 14 giorni, prodotto non usato.",
            contactPhone = "+39 333 1234567",
            contactEmail = "ciao@almanaam.it",
            openingHours = "Lun–Sab 9–19",
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var member = await MemberOfAsync(owner);
        var read = await member.Http.GetFromJsonAsync<JsonElement>("/api/v1/ai/policies");
        Assert.Equal(14, read.GetProperty("returnDays").GetInt32());
        Assert.Equal("+39 333 1234567", read.GetProperty("contactPhone").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(member, "/api/v1/ai/policies", new { returnDays = 30 })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(owner, "/api/v1/ai/policies", new { contactEmail = "not-an-email" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(owner, "/api/v1/ai/policies", new { returnDays = 400 })).StatusCode);

        var other = await OwnerAsync();
        var theirs = await other.Http.GetFromJsonAsync<JsonElement>("/api/v1/ai/policies");
        Assert.Equal(JsonValueKind.Null, theirs.GetProperty("returnPolicy").ValueKind);
    }

    [Fact]
    public async Task Shipping_zones_are_saved_as_a_list_kept_by_id_and_isolated_per_workspace()
    {
        var owner = await OwnerAsync();
        var first = await (await PutAsync(owner, "/api/v1/shipping/zones", new { zones = Zones })).Content.ReadFromJsonAsync<JsonElement>();
        var italia = first[0].GetProperty("id").GetString();
        Assert.Equal(["Italia", "Sardegna e isole"], first.EnumerateArray().Select(z => z.GetProperty("name").GetString()));
        Assert.Equal(5000, first[0].GetProperty("freeOver").GetInt64());

        // Same id with a new price, the second zone left out, a new one added.
        var second = await (await PutAsync(owner, "/api/v1/shipping/zones", new
        {
            zones = new object[]
            {
                new { id = italia, name = "Italia", price = 690, currency = "EUR" },
                new { name = "Europa", price = 1500, currency = "EUR", minDays = 5, maxDays = 10 },
            },
        })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(italia, second[0].GetProperty("id").GetString());
        Assert.Equal(690, second[0].GetProperty("price").GetInt64());
        Assert.Equal(["Italia", "Europa"], second.EnumerateArray().Select(z => z.GetProperty("name").GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(owner, "/api/v1/shipping/zones", new
        {
            zones = new object[] { new { name = "Italia", price = 100, currency = "EUR" }, new { name = "italia", price = 200, currency = "EUR" } },
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(owner, "/api/v1/shipping/zones", new
        {
            zones = new object[] { new { name = "Italia", price = 100, currency = "EUR", minDays = 5, maxDays = 2 } },
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(owner, "/api/v1/shipping/zones", new
        {
            zones = new object[] { new { name = "Italia", price = -1, currency = "eur" } },
        })).StatusCode);

        var member = await MemberOfAsync(owner);
        Assert.Equal(2, (await member.Http.GetFromJsonAsync<JsonElement>("/api/v1/shipping/zones")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(member, "/api/v1/shipping/zones", new { zones = Array.Empty<object>() })).StatusCode);

        // Another workspace sees none of these, and can't change them by sending their id.
        var other = await OwnerAsync();
        Assert.Equal(0, (await other.Http.GetFromJsonAsync<JsonElement>("/api/v1/shipping/zones")).GetArrayLength());
        var taken = await (await PutAsync(other, "/api/v1/shipping/zones", new
        {
            zones = new object[] { new { id = italia, name = "Mine", price = 0, currency = "EUR" } },
        })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(italia, taken[0].GetProperty("id").GetString());
        Assert.Equal(690, (await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/shipping/zones"))[0].GetProperty("price").GetInt64());
    }

    [Fact]
    public async Task The_ai_quotes_shipping_costs_only_from_the_zones()
    {
        var owner = await OwnerAsync();
        await PutAsync(owner, "/api/v1/shipping/zones", new { zones = Zones });
        var marker = Marker();
        Api.Ai.Respond = c => !Mine(c, marker) ? FakeAiAnswer.Say("ok")
            : c.ToolResults.Count == 0 ? FakeAiAnswer.Call("get_shipping_options", "{}")
            : FakeAiAnswer.Say("In Sardegna la spedizione costa €9.90, gratis sopra €80.00, in 3-6 giorni.");

        var result = await TestAsync(owner, $"Quanto costa spedire in Sardegna? {marker}");

        Assert.Equal("In Sardegna la spedizione costa €9.90, gratis sopra €80.00, in 3-6 giorni.", result.GetProperty("reply").GetString());
        var tool = Api.Ai.Calls.Last(c => Mine(c, marker)).ToolResults.Single();
        Assert.Contains("9.90 EUR", tool, StringComparison.Ordinal);
        Assert.Contains("3-6 days", tool, StringComparison.Ordinal);
        var source = Assert.Single(result.GetProperty("sources").EnumerateArray());
        Assert.Equal("shipping", source.GetProperty("kind").GetString());

        // A made-up shipping cost is held back.
        var invented = Marker();
        Api.Ai.Respond = c => !Mine(c, invented) ? FakeAiAnswer.Say("ok")
            : c.ToolResults.Count == 0 ? FakeAiAnswer.Call("get_shipping_options", "{}")
            : FakeAiAnswer.Say("Per la Sicilia sono €7.50.");
        Assert.Equal("price_not_from_tool", (await TestAsync(owner, $"E la Sicilia? {invented}")).GetProperty("blocked").GetString());
    }

    [Fact]
    public async Task Without_zones_the_ai_is_told_to_hand_off()
    {
        var owner = await OwnerAsync();
        var marker = Marker();
        Api.Ai.Respond = c => !Mine(c, marker) ? FakeAiAnswer.Say("ok")
            : c.ToolResults.Count == 0 ? FakeAiAnswer.Call("get_shipping_options", "{}")
            : FakeAiAnswer.Call("handoff_to_human", """{"reason":"unknown_answer","question":"Costo spedizione"}""");

        var result = await TestAsync(owner, $"Quanto costa la spedizione? {marker}");

        Assert.Contains("unknown_answer", Api.Ai.Calls.Last(c => Mine(c, marker)).ToolResults.Single(), StringComparison.Ordinal);
        Assert.Equal("unknown_answer", result.GetProperty("handoff").GetString());
        Assert.Empty(result.GetProperty("sources").EnumerateArray());
    }

    [Fact]
    public async Task The_ai_answers_from_policies_and_faq_and_shows_its_sources()
    {
        var owner = await OwnerAsync();
        await PutAsync(owner, "/api/v1/ai/policies", new
        {
            returnDays = 14,
            returnPolicy = "Reso entro 14 giorni. La spedizione del reso costa €5.00.",
        });
        await owner.PostAsync("/api/v1/ai/answers", new { question = "Posso cambiare la taglia?", answer = "Sì, il cambio taglia è gratuito." });
        var marker = Marker();
        Api.Ai.Respond = c => !Mine(c, marker) ? FakeAiAnswer.Say("ok")
            : c.ToolResults.Count == 0 ? FakeAiAnswer.Call("search_knowledge", """{"query":"reso cambio taglia","topic":"returns"}""")
            : FakeAiAnswer.Say("Puoi rendere entro 14 giorni (il reso costa €5.00) e il cambio taglia è gratuito.");

        var result = await TestAsync(owner, $"Posso fare un reso o cambiare taglia? {marker}");

        // The €5.00 comes from the owner's own policy text, so it may be quoted.
        Assert.Equal("Puoi rendere entro 14 giorni (il reso costa €5.00) e il cambio taglia è gratuito.", result.GetProperty("reply").GetString());
        var tool = Api.Ai.Calls.Last(c => Mine(c, marker)).ToolResults.Single();
        Assert.Contains("returnWithinDaysOfDelivery", tool, StringComparison.Ordinal);
        Assert.Contains("cambio taglia", tool, StringComparison.Ordinal);
        Assert.Equal(
            [("policy", "returns"), ("answer", "Posso cambiare la taglia?")],
            result.GetProperty("sources").EnumerateArray().Select(s => (s.GetProperty("kind").GetString(), s.GetProperty("label").GetString())));
    }

    [Fact]
    public async Task Payment_is_cash_on_delivery_and_contact_details_come_from_the_policies()
    {
        var owner = await OwnerAsync();
        await PutAsync(owner, "/api/v1/ai/policies", new { contactPhone = "+39 333 1234567", openingHours = "Lun–Sab 9–19" });
        var marker = Marker();
        Api.Ai.Respond = c => !Mine(c, marker) ? FakeAiAnswer.Say("ok")
            : c.ToolResults.Count == 0 ? FakeAiAnswer.Call("search_knowledge", """{"query":"pagamento","topic":"payment"}""")
            : c.ToolResults.Count == 1 ? FakeAiAnswer.Call("search_knowledge", """{"query":"telefono","topic":"contact"}""")
            : FakeAiAnswer.Say("Paghi in contanti alla consegna. Ci trovi al +39 333 1234567, Lun–Sab 9–19.");

        var result = await TestAsync(owner, $"Come pago? E come vi chiamo? {marker}");

        var tools = Api.Ai.Calls.Last(c => Mine(c, marker)).ToolResults;
        Assert.Contains("cash on delivery", tools[0], StringComparison.Ordinal);
        Assert.Contains("+39 333 1234567", tools[1], StringComparison.Ordinal);
        Assert.NotNull(result.GetProperty("reply").GetString());
        Assert.Equal(["payment", "contact"], result.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task Answers_say_whether_they_were_taught_or_added()
    {
        var owner = await OwnerAsync();
        await owner.PostAsync("/api/v1/ai/answers", new { question = "Orari?", answer = "Lun–Sab 9–19" });

        var answers = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/ai/answers");

        Assert.Equal("added", Assert.Single(answers.EnumerateArray()).GetProperty("source").GetString());
    }

    private static string Marker() => "#" + Guid.NewGuid().ToString("N")[..8];

    private static bool Mine(FakeAiCall call, string marker) =>
        call.Messages.Any(m => m.Text?.Contains(marker, StringComparison.Ordinal) == true);

    private static async Task<JsonElement> TestAsync(ApiClient owner, string text)
    {
        var response = await owner.PostAsync("/api/v1/ai/test", new { messages = new[] { new { author = "customer", text } } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<ApiClient> OwnerAsync()
    {
        var client = Api.CreateApiClient();
        (await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), "Almanaam")).EnsureSuccessStatusCode();
        return client;
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

    private static async Task<HttpResponseMessage> PutAsync(ApiClient client, string url, object body)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PutAsJsonAsync(url, body);
    }
}
