using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Inbox;

/// <summary>The inbox hub: a dashboard hears its own workspace only (ADR 0016, CLAUDE.md rule 1).</summary>
[Collection(AuthTests.Name)]
public sealed class InboxRealtimeTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Changes_reach_the_own_workspace_only_and_carry_ids_only()
    {
        var inbox = new InboxTests(fixture);
        var emailA = AuthFixture.NewEmail();
        var emailB = AuthFixture.NewEmail();
        var workspaceA = await SignupAsync(emailA, "Almanaam");
        await SignupAsync(emailB, "Bottega Blu");

        await using var dashboardA = await ConnectAsync(emailA);
        await using var dashboardB = await ConnectAsync(emailB);
        var heardA = Listen(dashboardA);
        var heardB = Listen(dashboardB);

        var received = await inbox.ReceiveAsync(workspaceA, "visitor-rt", "Realtime hello");

        var payload = await heardA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(received.ConversationId.ToString(), payload.GetProperty("conversationId").GetString());
        Assert.Single(payload.EnumerateObject());
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(heardB.Task.IsCompleted, "Another workspace heard about the change.");
    }

    [Fact]
    public async Task Mentions_reach_only_the_mentioned_member_and_carry_ids_only()
    {
        var inbox = new InboxTests(fixture);
        var notes = new InboxNotesTests(fixture);
        var ownerEmail = AuthFixture.NewEmail();
        var owner = Api.CreateApiClient();
        var me = await (await owner.SignupAndVerifyAsync(Api.Emails, ownerEmail, "Almanaam")).Content.ReadFromJsonAsync<JsonElement>();
        var workspace = me.GetProperty("workspace").GetProperty("id").GetString()!;
        var (member, memberId) = await notes.MemberOfAsync(owner);
        var memberEmail = (await member.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("user").GetProperty("email").GetString()!;
        var id = (await inbox.ReceiveAsync(workspace, "visitor-mention", "Ciao")).ConversationId;

        await using var ownerDashboard = await ConnectAsync(ownerEmail);
        await using var memberDashboard = await ConnectAsync(memberEmail);
        var ownerHeard = Listen(ownerDashboard, "mentioned");
        var memberHeard = Listen(memberDashboard, "mentioned");

        var note = await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "@Luca please call her", mentionedUserIds = new[] { memberId } });
        var noteId = (await note.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var payload = await memberHeard.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(id.ToString(), payload.GetProperty("conversationId").GetString());
        Assert.Equal(noteId, payload.GetProperty("noteId").GetString());
        Assert.Equal(2, payload.EnumerateObject().Count());
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(ownerHeard.Task.IsCompleted, "A member who wasn't mentioned heard the mention.");
    }

    [Fact]
    public async Task Typing_reaches_the_own_workspace_only_and_carries_ids_only()
    {
        var inbox = new InboxTests(fixture);
        var emailA = AuthFixture.NewEmail();
        var emailB = AuthFixture.NewEmail();
        var ownerA = Api.CreateApiClient();
        var me = await (await ownerA.SignupAndVerifyAsync(Api.Emails, emailA, "Almanaam")).Content.ReadFromJsonAsync<JsonElement>();
        var workspaceA = me.GetProperty("workspace").GetProperty("id").GetString()!;
        var userId = me.GetProperty("user").GetProperty("id").GetString();
        await SignupAsync(emailB, "Bottega Blu");
        var id = (await inbox.ReceiveAsync(workspaceA, "visitor-typing", "Ciao")).ConversationId;

        await using var dashboardA = await ConnectAsync(emailA);
        await using var dashboardB = await ConnectAsync(emailB);
        var heardA = Listen(dashboardA, "typing");
        var heardB = Listen(dashboardB, "typing");

        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await ownerA.PostAsync($"/api/v1/conversations/{id}/typing")).StatusCode);

        var payload = await heardA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(id.ToString(), payload.GetProperty("conversationId").GetString());
        Assert.Equal(userId, payload.GetProperty("userId").GetString());
        Assert.Equal(2, payload.EnumerateObject().Count());
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(heardB.Task.IsCompleted, "Another workspace heard about the typing.");

        using var other = Api.CreateApiClient();
        await other.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), "Terza Bottega");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await other.PostAsync($"/api/v1/conversations/{id}/typing")).StatusCode);
    }

    [Fact]
    public async Task Hub_needs_a_session()
    {
        await using var connection = Build(cookies: string.Empty, csrfToken: string.Empty);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => connection.StartAsync());
    }

    private async Task<string> SignupAsync(string email, string business)
    {
        using var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, email, business)).Content.ReadFromJsonAsync<JsonElement>();
        return me.GetProperty("workspace").GetProperty("id").GetString()!;
    }

    private static TaskCompletionSource<JsonElement> Listen(HubConnection connection, string method = "conversationChanged")
    {
        var heard = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>(method, payload => heard.TrySetResult(payload));
        return heard;
    }

    /// <summary>Signs in like the dashboard and opens the hub with that session's cookies and CSRF token.</summary>
    private async Task<HubConnection> ConnectAsync(string email)
    {
        using var http = Api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        var jar = new Dictionary<string, string>(StringComparer.Ordinal);

        var token = await CsrfAsync(http, jar);
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login"))
        {
            login.Content = JsonContent.Create(new { email, password = ApiClient.DefaultPassword, rememberMe = false });
            login.Headers.Add(ApiSecurityExtensions.CsrfHeaderName, token);
            Send(login, jar);
            using var response = await http.SendAsync(login);
            response.EnsureSuccessStatusCode();
            Keep(response, jar);
        }

        // Tokens are bound to the user: fetch one for the new session.
        token = await CsrfAsync(http, jar);
        var connection = Build(string.Join("; ", jar.Select(c => $"{c.Key}={c.Value}")), token);
        await connection.StartAsync();
        return connection;
    }

    private HubConnection Build(string cookies, string csrfToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri("https://localhost/api/v1/hubs/inbox"), options =>
            {
                options.HttpMessageHandlerFactory = _ => Api.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (cookies.Length > 0)
                {
                    options.Headers["Cookie"] = cookies;
                    options.Headers[ApiSecurityExtensions.CsrfHeaderName] = csrfToken;
                }
            })
            .Build();

    private static async Task<string> CsrfAsync(HttpClient http, Dictionary<string, string> jar)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/csrf");
        Send(request, jar);
        using var response = await http.SendAsync(request);
        Keep(response, jar);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static void Send(HttpRequestMessage request, Dictionary<string, string> jar)
    {
        if (jar.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", jar.Select(c => $"{c.Key}={c.Value}")));
        }
    }

    private static void Keep(HttpResponseMessage response, Dictionary<string, string> jar)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        foreach (var cookie in cookies)
        {
            var pair = cookie.Split(';', 2)[0];
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            jar[pair[..separator]] = pair[(separator + 1)..];
        }
    }
}
