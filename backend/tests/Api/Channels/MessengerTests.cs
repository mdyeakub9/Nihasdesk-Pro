using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.TestSupport;
using Npgsql;

namespace Nihasdesk.Api.Tests.Channels;

/// <summary>Messenger end to end against a fake Graph API (ADR 0017, SEC-06/17/28/43).</summary>
[Collection(AuthTests.Name)]
public sealed class MessengerTests(AuthFixture fixture)
{
    private const string Webhook = "/api/v1/webhooks/meta";

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Webhook_setup_check_answers_only_with_our_verify_token()
    {
        using var meta = Api.CreateApiClient();
        var ok = await meta.Http.GetAsync($"{Webhook}?hub.mode=subscribe&hub.verify_token={NihasdeskApiFactory.MetaVerifyToken}&hub.challenge=1158201444");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("1158201444", await ok.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await meta.Http.GetAsync($"{Webhook}?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await meta.Http.GetAsync(
            $"{Webhook}?hub.mode=subscribe&hub.verify_token={NihasdeskApiFactory.MetaVerifyToken}&hub.challenge=%3Cscript%3E")).StatusCode);
    }

    [Fact]
    public async Task Unsigned_or_wrongly_signed_webhooks_are_rejected_and_not_stored()
    {
        var (owner, _, pageId) = await ConnectedOwnerAsync();
        var body = MessageWebhook(pageId, "5001", "m-unsigned", "Hello?");

        Assert.Equal(HttpStatusCode.Forbidden, (await PostWebhookAsync(body, signature: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostWebhookAsync(body, signature: "sha256=" + new string('0', 64))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostWebhookAsync(body, Sign(body, "another-app-secret"))).StatusCode);

        await Api.ProcessMetaWebhooksAsync();
        Assert.Empty((await ListAsync(owner)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Owner_connects_a_page_and_the_token_is_never_shown_and_stored_encrypted()
    {
        var pageId = NewPageId();
        Api.Meta.Pages.Clear();
        Api.Meta.Pages.Add((pageId, "Bottega Verde", "page-token-" + pageId));
        var (owner, workspace) = await OwnerAsync();

        var redirect = await ConnectAsync(owner);
        Assert.Equal($"{NihasdeskApiFactory.DashboardUrl}/settings/channels?connected=1&elsewhere=0", redirect);

        var listed = await owner.Http.GetStringAsync("/api/v1/channels");
        Assert.Contains("Bottega Verde", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("page-token", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("token", listed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Api.Meta.Calls, c => c.Method == HttpMethod.Post && c.Path.EndsWith($"/{pageId}/subscribed_apps", StringComparison.Ordinal));

        var stored = await StoredTokenAsync(workspace, pageId);
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.DoesNotContain("page-token", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Members_cannot_manage_channels_and_a_foreign_state_is_refused()
    {
        var (owner, _) = await OwnerAsync();
        var (other, _) = await OwnerAsync("Bottega Blu");

        var start = await (await owner.PostAsync("/api/v1/channels/meta/connect")).Content.ReadFromJsonAsync<JsonElement>();
        var url = new Uri(start.GetProperty("url").GetString()!);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("tests-app", query["client_id"]);
        Assert.Equal($"{NihasdeskApiFactory.DashboardUrl}/api/v1/channels/meta/callback", query["redirect_uri"]);

        // Another workspace's owner replays the state: refused, nothing connected.
        using var replay = Api.CreateApiClient(followRedirects: false);
        await replay.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), "Ladro");
        var stolen = await replay.Http.GetAsync($"/api/v1/channels/meta/callback?code=abc&state={Uri.EscapeDataString(query["state"]!)}");
        Assert.EndsWith("error=invalid_state", stolen.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal("[]", await other.Http.GetStringAsync("/api/v1/channels"));

        var member = await MemberOfAsync(owner);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Http.GetAsync("/api/v1/channels")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync("/api/v1/channels/meta/connect")).StatusCode);
    }

    [Fact]
    public async Task A_page_belongs_to_one_workspace_only()
    {
        var pageId = NewPageId();
        Api.Meta.Pages.Clear();
        Api.Meta.Pages.Add((pageId, "Shared Page", "page-token-" + pageId));
        var (first, _) = await OwnerAsync();
        var (second, _) = await OwnerAsync("Bottega Blu");

        Assert.EndsWith("connected=1&elsewhere=0", await ConnectAsync(first), StringComparison.Ordinal);
        Assert.EndsWith("connected=0&elsewhere=1", await ConnectAsync(second), StringComparison.Ordinal);
        Assert.Equal("[]", await second.Http.GetStringAsync("/api/v1/channels"));
    }

    [Fact]
    public async Task Customer_messages_arrive_once_and_team_replies_go_out_through_the_page()
    {
        var (owner, _, pageId) = await ConnectedOwnerAsync();
        var psid = NewPageId();
        var body = MessageWebhook(pageId, psid, "m-" + psid, "Ciao, avete la taglia M?");

        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(body, Sign(body))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(body, Sign(body))).StatusCode);
        var echo = MessageWebhook(pageId, psid, "m-echo-" + psid, "Sent from Facebook", echo: true);
        await PostWebhookAsync(echo, Sign(echo));
        await Api.ProcessMetaWebhooksAsync();

        var conversation = Assert.Single((await ListAsync(owner)).GetProperty("items").EnumerateArray());
        Assert.Equal("messenger", conversation.GetProperty("channel").GetString());
        Assert.Equal("Giulia Rossi", conversation.GetProperty("contactName").GetString());
        var id = conversation.GetProperty("id").GetString();
        var history = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages")).GetProperty("items");
        Assert.Single(history.EnumerateArray());

        var reply = await owner.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "Sì, la M è disponibile." });
        Assert.Equal("pending", (await reply.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("delivery").GetString());

        Assert.Equal(1, await Api.SendRepliesAsync());
        var send = Assert.Single(Api.Meta.Sends, s => s.Body.Contains(psid, StringComparison.Ordinal));
        Assert.EndsWith($"/{pageId}/messages", send.Path, StringComparison.Ordinal);
        Assert.Equal($"Bearer page-token-{pageId}", send.Authorization);
        using (var sent = JsonDocument.Parse(send.Body))
        {
            Assert.Equal("RESPONSE", sent.RootElement.GetProperty("messaging_type").GetString());
            Assert.Equal("Sì, la M è disponibile.", sent.RootElement.GetProperty("message").GetProperty("text").GetString());
        }

        var messages = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages")).GetProperty("items");
        Assert.Equal("sent", messages.EnumerateArray().Last().GetProperty("delivery").GetString());
        Assert.Equal(0, await Api.SendRepliesAsync());
    }

    [Fact]
    public async Task Replies_respect_the_24_hour_window()
    {
        var (owner, _, pageId) = await ConnectedOwnerAsync();
        var psid = NewPageId();
        // The customer wrote 25 hours ago (the clock itself stays, so the session stays valid).
        var body = MessageWebhook(pageId, psid, "m-" + psid, "Hello", age: TimeSpan.FromHours(25));
        await PostWebhookAsync(body, Sign(body));
        await Api.ProcessMetaWebhooksAsync();
        var id = (await ListAsync(owner)).GetProperty("items")[0].GetProperty("id").GetString();

        var late = await owner.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "Sorry for the delay" });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("conversation.outside_window", await AuthFlowTests.CodeOf(late));
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "Call her instead" })).StatusCode);
    }

    [Fact]
    public async Task Team_notes_never_reach_the_channel_even_when_resolved_or_mentioning()
    {
        var (owner, _, pageId) = await ConnectedOwnerAsync();
        var id = await ConversationAsync(owner, pageId);
        var me = (await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("user").GetProperty("id").GetString();
        var sendsBefore = Api.Meta.Sends.Count;

        var note = await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "SECRET-NOTE-FOR-TEAM", mentionedUserIds = new[] { me } });
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        var created = await note.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("none", created.GetProperty("delivery").GetString());
        await owner.RefreshCsrfAsync();
        await owner.Http.PutAsync($"/api/v1/conversations/{id}/notes/{created.GetProperty("id").GetString()}/resolve", null);

        Assert.Equal(0, await Api.SendRepliesAsync());
        Assert.Equal(sendsBefore, Api.Meta.Sends.Count);
        Assert.DoesNotContain(Api.Meta.Sends, s => s.Body.Contains("SECRET-NOTE-FOR-TEAM", StringComparison.Ordinal));
    }

    // Product cards go out as a carousel now: Channels/RichMessagesTests (ADR 0032).

    [Fact]
    public async Task The_sender_checks_the_window_again_before_sending()
    {
        var (owner, _, pageId) = await ConnectedOwnerAsync();
        var id = await ConversationAsync(owner, pageId);
        await owner.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "Queued just in time" });
        var sendsBefore = Api.Meta.Sends.Count;

        // The reply waits (Meta outage, …) until the window has closed.
        Api.Clock.Advance(TimeSpan.FromHours(25));
        try
        {
            await Api.SendRepliesAsync();
        }
        finally
        {
            Api.Clock.Advance(TimeSpan.FromHours(-25));
        }

        Assert.Equal(sendsBefore, Api.Meta.Sends.Count);
        Assert.Equal("failed", await LastDeliveryAsync(owner, id));
    }

    [Fact]
    public async Task Temporary_send_errors_are_retried_and_permanent_ones_fail()
    {
        var (owner, _, pageId) = await ConnectedOwnerAsync();
        var id = await ConversationAsync(owner, pageId);

        Api.Meta.SendFailure = (HttpStatusCode.InternalServerError, 2);
        try
        {
            await owner.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "First try" });
            await Api.SendRepliesAsync();
            Assert.Equal("pending", await LastDeliveryAsync(owner, id));

            Api.Meta.SendFailure = (HttpStatusCode.BadRequest, 551);
            Api.Clock.Advance(TimeSpan.FromMinutes(1));
            await Api.SendRepliesAsync();
            Assert.Equal("failed", await LastDeliveryAsync(owner, id));
        }
        finally
        {
            Api.Meta.SendFailure = null;
        }
    }

    [Fact]
    public async Task Disconnecting_wipes_the_token_and_stops_new_messages()
    {
        var (owner, workspace, pageId) = await ConnectedOwnerAsync();
        var connection = (await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/channels"))[0].GetProperty("id").GetString();

        await owner.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync($"/api/v1/channels/{connection}")).StatusCode);
        Assert.Equal("[]", await owner.Http.GetStringAsync("/api/v1/channels"));
        Assert.Equal(string.Empty, await StoredTokenAsync(workspace, pageId));
        Assert.Contains(Api.Meta.Calls, c => c.Method == HttpMethod.Delete && c.Path.EndsWith($"/{pageId}/subscribed_apps", StringComparison.Ordinal));

        var body = MessageWebhook(pageId, NewPageId(), "m-after-" + pageId, "Still there?");
        await PostWebhookAsync(body, Sign(body));
        await Api.ProcessMetaWebhooksAsync();
        Assert.Empty((await ListAsync(owner)).GetProperty("items").EnumerateArray());

        var (other, _) = await OwnerAsync("Bottega Blu");
        await other.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.DeleteAsync($"/api/v1/channels/{connection}")).StatusCode);
    }

    private async Task<(ApiClient Owner, string Workspace, string PageId)> ConnectedOwnerAsync()
    {
        var pageId = NewPageId();
        Api.Meta.Pages.Clear();
        Api.Meta.Pages.Add((pageId, "Page " + pageId, "page-token-" + pageId));
        var (owner, workspace) = await OwnerAsync();
        Assert.EndsWith("connected=1&elsewhere=0", await ConnectAsync(owner), StringComparison.Ordinal);
        return (owner, workspace, pageId);
    }

    private async Task<(ApiClient Client, string Workspace)> OwnerAsync(string business = "Almanaam")
    {
        var client = Api.CreateApiClient(followRedirects: false);
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), business)).Content.ReadFromJsonAsync<JsonElement>();
        return (client, me.GetProperty("workspace").GetProperty("id").GetString()!);
    }

    private async Task<ApiClient> MemberOfAsync(ApiClient owner)
    {
        var email = AuthFixture.NewEmail();
        await owner.PostAsync("/api/v1/team/invitations", new { email, role = "member" });
        var token = HttpUtility.ParseQueryString(new Uri(
            System.Text.RegularExpressions.Regex.Match(Api.Emails.LastTo(email).TextBody, @"https://\S+accept-invitation\?token=\S+").Value).Query)["token"];
        var member = Api.CreateApiClient();
        (await member.PostAsync("/api/v1/invitations/accept", new { token, name = "Luca", password = "girasole tramonto barca verde" })).EnsureSuccessStatusCode();
        return member;
    }

    /// <summary>Runs the Facebook Login round trip and returns where the browser is sent.</summary>
    private static async Task<string> ConnectAsync(ApiClient owner)
    {
        var start = await (await owner.PostAsync("/api/v1/channels/meta/connect")).Content.ReadFromJsonAsync<JsonElement>();
        var state = HttpUtility.ParseQueryString(new Uri(start.GetProperty("url").GetString()!).Query)["state"]!;
        var callback = await owner.Http.GetAsync($"/api/v1/channels/meta/callback?code=fake-code&state={Uri.EscapeDataString(state)}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return callback.Headers.Location!.ToString();
    }

    private async Task<string> ConversationAsync(ApiClient owner, string pageId)
    {
        var psid = NewPageId();
        var body = MessageWebhook(pageId, psid, "m-" + psid, "Hi");
        await PostWebhookAsync(body, Sign(body));
        await Api.ProcessMetaWebhooksAsync();
        return (await ListAsync(owner)).GetProperty("items")[0].GetProperty("id").GetString()!;
    }

    private static async Task<string?> LastDeliveryAsync(ApiClient owner, string conversationId) =>
        (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversationId}/messages"))
            .GetProperty("items").EnumerateArray().Last().GetProperty("delivery").GetString();

    private async Task<HttpResponseMessage> PostWebhookAsync(string body, string? signature)
    {
        using var meta = Api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Post, Webhook) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (signature is not null)
        {
            request.Headers.Add("X-Hub-Signature-256", signature);
        }

        return await meta.SendAsync(request);
    }

    private static Task<JsonElement> ListAsync(ApiClient client) =>
        client.Http.GetFromJsonAsync<JsonElement>("/api/v1/conversations");

    private string MessageWebhook(string pageId, string psid, string mid, string text, bool echo = false, TimeSpan age = default) =>
        JsonSerializer.Serialize(new
        {
            @object = "page",
            entry = new[]
            {
                new
                {
                    id = pageId,
                    time = Api.Clock.GetUtcNow().ToUnixTimeMilliseconds(),
                    messaging = new[]
                    {
                        new
                        {
                            sender = new { id = echo ? pageId : psid },
                            recipient = new { id = echo ? psid : pageId },
                            timestamp = (Api.Clock.GetUtcNow() - age).ToUnixTimeMilliseconds(),
                            message = new { mid, text, is_echo = echo },
                        },
                    },
                },
            },
        });

    private static string Sign(string body, string secret = FakeMetaGraphHandler.AppSecret) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    private static string NewPageId() => Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The stored token column, read as the app role inside the workspace (RLS applies).</summary>
    private async Task<string?> StoredTokenAsync(string workspace, string pageId)
    {
        await using var connection = new NpgsqlConnection(fixture.Database.AppConnectionString);
        await connection.OpenAsync();
        await using (var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, false)", connection))
        {
            setTenant.Parameters.AddWithValue("t", workspace);
            await setTenant.ExecuteNonQueryAsync();
        }

        await using var read = new NpgsqlCommand(
            "SELECT protected_token FROM channels.channel_connections WHERE external_account_id = @p ORDER BY connected_at DESC LIMIT 1",
            connection);
        read.Parameters.AddWithValue("p", pageId);
        return (string?)await read.ExecuteScalarAsync();
    }
}
