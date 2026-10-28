using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.TestSupport;
using Npgsql;

namespace Nihasdesk.Api.Tests.Team;

/// <summary>Invitations and team management against docs/standards/permissions.md (SEC-06, SEC-10, ADR 0014).</summary>
[Collection(AuthTests.Name)]
public sealed partial class TeamTests(AuthFixture fixture)
{
    private const string Password = "girasole tramonto barca verde";

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Owner_invites_a_member_who_joins_with_a_new_account()
    {
        var (owner, workspace) = await OwnerAsync("Bottega Verde");
        var email = AuthFixture.NewEmail();

        var invite = await owner.PostAsync("/api/v1/team/invitations", new { email, role = "member" });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var mail = Api.Emails.LastTo(email);
        Assert.Contains("Bottega Verde", mail.TextBody, StringComparison.Ordinal);

        using var guest = Api.CreateApiClient();
        var token = TokenFrom(mail.TextBody);
        var preview = await (await guest.PostAsync("/api/v1/invitations/preview", new { token })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bottega Verde", preview.GetProperty("workspaceName").GetString());
        Assert.Equal("member", preview.GetProperty("role").GetString());

        var accepted = await guest.PostAsync("/api/v1/invitations/accept", new { token, name = "Luca", password = Password });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var me = await guest.Http.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(workspace, me.GetProperty("workspace").GetProperty("id").GetString());
        Assert.Equal("member", me.GetProperty("role").GetString());
        Assert.True(me.GetProperty("user").GetProperty("emailVerified").GetBoolean());

        var members = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/team/members");
        Assert.Equal(["owner", "member"], members.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
    }

    [Fact]
    public async Task Members_can_see_the_team_but_change_nothing()
    {
        var (owner, _) = await OwnerAsync();
        var (member, _) = await JoinAsync(owner, "member");
        var (other, otherId) = await JoinAsync(owner, "member");

        Assert.Equal(HttpStatusCode.OK, (await member.Http.GetAsync("/api/v1/team/members")).StatusCode);
        await AssertForbidden(member.Http.GetAsync("/api/v1/team/invitations"));
        await AssertForbidden(member.PostAsync("/api/v1/team/invitations", new { email = AuthFixture.NewEmail(), role = "member" }));
        await AssertForbidden(Patch(member, otherId, "admin"));
        await AssertForbidden(Delete(member, $"/api/v1/team/members/{otherId}"));
        other.Dispose();
    }

    [Fact]
    public async Task Admins_invite_and_remove_members_but_not_admins_or_roles()
    {
        var (owner, _) = await OwnerAsync();
        var (admin, _) = await JoinAsync(owner, "admin");
        var (_, memberId) = await JoinAsync(owner, "member");
        var (_, otherAdminId) = await JoinAsync(owner, "admin");
        var ownerId = await UserIdAsync(owner);

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync("/api/v1/team/invitations", new { email = AuthFixture.NewEmail(), role = "member" })).StatusCode);
        await AssertForbidden(admin.PostAsync("/api/v1/team/invitations", new { email = AuthFixture.NewEmail(), role = "admin" }));
        await AssertForbidden(Patch(admin, memberId, "admin"));
        await AssertForbidden(Delete(admin, $"/api/v1/team/members/{otherAdminId}"));
        Assert.Equal("team.owner_is_fixed", await AuthFlowTests.CodeOf(await Delete(admin, $"/api/v1/team/members/{ownerId}")));
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, $"/api/v1/team/members/{memberId}")).StatusCode);
    }

    [Fact]
    public async Task Owner_changes_roles_and_the_change_applies_immediately()
    {
        var (owner, _) = await OwnerAsync();
        var (admin, adminId) = await JoinAsync(owner, "admin");
        var ownerId = await UserIdAsync(owner);

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync("/api/v1/team/invitations", new { email = AuthFixture.NewEmail(), role = "member" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Patch(owner, adminId, "member")).StatusCode);

        // Same session, no re-login: the role is read from the database on each request.
        await AssertForbidden(admin.PostAsync("/api/v1/team/invitations", new { email = AuthFixture.NewEmail(), role = "member" }));
        Assert.Equal("team.owner_is_fixed", await AuthFlowTests.CodeOf(await Patch(owner, ownerId, "member")));
    }

    [Fact]
    public async Task Removed_members_are_signed_out_and_can_no_longer_enter()
    {
        var (owner, _) = await OwnerAsync();
        var (member, memberId) = await JoinAsync(owner, "member");
        var memberEmail = (await member.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("user").GetProperty("email").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(owner, $"/api/v1/team/members/{memberId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await member.Http.GetAsync("/api/v1/me")).StatusCode);
        using var again = Api.CreateApiClient();
        Assert.Equal("auth.no_workspace", await AuthFlowTests.CodeOf(await again.LoginAsync(memberEmail, Password)));
    }

    [Fact]
    public async Task Another_workspaces_invitations_and_members_are_not_found()
    {
        var (ownerA, _) = await OwnerAsync("Shop A");
        var (ownerB, _) = await OwnerAsync("Shop B");
        var invitationA = await (await ownerA.PostAsync("/api/v1/team/invitations", new { email = AuthFixture.NewEmail(), role = "member" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var (_, memberA) = await JoinAsync(ownerA, "member");
        var invitationId = invitationA.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.PostAsync($"/api/v1/team/invitations/{invitationId}/resend")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(ownerB, $"/api/v1/team/invitations/{invitationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Patch(ownerB, memberA, "admin")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(ownerB, $"/api/v1/team/members/{memberA}")).StatusCode);
        var membersB = await ownerB.Http.GetFromJsonAsync<JsonElement>("/api/v1/team/members");
        Assert.Single(membersB.EnumerateArray());
    }

    [Fact]
    public async Task Invitation_links_work_once_expire_after_48_hours_and_die_on_resend_or_revoke()
    {
        var (owner, _) = await OwnerAsync();
        using var guest = Api.CreateApiClient();

        var used = await InviteAsync(owner, AuthFixture.NewEmail());
        Assert.Equal(HttpStatusCode.OK, (await guest.PostAsync("/api/v1/invitations/accept", new { token = used.Token, name = "A", password = Password })).StatusCode);
        Assert.Equal("team.invitation_invalid", await PreviewCode(used.Token));

        var resent = await InviteAsync(owner, AuthFixture.NewEmail());
        await owner.PostAsync($"/api/v1/team/invitations/{resent.Id}/resend");
        Assert.Equal("team.invitation_invalid", await PreviewCode(resent.Token));
        Assert.Null(await PreviewCode(TokenFrom(Api.Emails.LastTo(resent.Email).TextBody)));

        var revoked = await InviteAsync(owner, AuthFixture.NewEmail());
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(owner, $"/api/v1/team/invitations/{revoked.Id}")).StatusCode);
        Assert.Equal("team.invitation_invalid", await PreviewCode(revoked.Token));

        var late = await InviteAsync(owner, AuthFixture.NewEmail());
        Api.Clock.Advance(TimeSpan.FromHours(48) + TimeSpan.FromMinutes(1));
        Assert.Equal("team.invitation_invalid", await PreviewCode(late.Token));
        Assert.Equal("team.invitation_invalid", await PreviewCode("made-up-token"));
    }

    [Fact]
    public async Task Only_a_hash_of_the_token_is_stored()
    {
        var (owner, workspace) = await OwnerAsync();
        var invite = await InviteAsync(owner, AuthFixture.NewEmail());

        await using var connection = new NpgsqlConnection(fixture.Database.AppConnectionString);
        await connection.OpenAsync();
        await using (var tenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, false)", connection))
        {
            tenant.Parameters.AddWithValue("t", workspace);
            await tenant.ExecuteNonQueryAsync();
        }

        await using var read = new NpgsqlCommand("SELECT row_to_json(i)::text, token_hash FROM identity.invitations i WHERE id = @id", connection);
        read.Parameters.AddWithValue("id", Guid.Parse(invite.Id));
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.DoesNotContain(invite.Token, reader.GetString(0), StringComparison.Ordinal);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(invite.Token)), (byte[])reader[1]);
    }

    [Fact]
    public async Task Existing_accounts_and_current_members_cannot_be_invited_in()
    {
        var (owner, _) = await OwnerAsync();
        var existing = AuthFixture.NewEmail();
        using (var other = Api.CreateApiClient())
        {
            (await other.SignupAndVerifyAsync(Api.Emails, existing, "Elsewhere")).EnsureSuccessStatusCode();
        }

        var invite = await InviteAsync(owner, existing);
        using var guest = Api.CreateApiClient();
        var accept = await guest.PostAsync("/api/v1/invitations/accept", new { token = invite.Token, name = "X", password = Password });
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
        Assert.Equal("team.email_has_account", await AuthFlowTests.CodeOf(accept));

        var ownerEmail = (await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("user").GetProperty("email").GetString();
        var again = await owner.PostAsync("/api/v1/team/invitations", new { email = ownerEmail, role = "member" });
        Assert.Equal("team.already_member", await AuthFlowTests.CodeOf(again));
    }

    [Fact]
    public async Task Owner_cannot_be_invited_and_roles_are_validated()
    {
        var (owner, _) = await OwnerAsync();

        var asOwner = await owner.PostAsync("/api/v1/team/invitations", new { email = AuthFixture.NewEmail(), role = "owner" });

        Assert.Equal(HttpStatusCode.BadRequest, asOwner.StatusCode);
        Assert.Equal("validation.failed", await AuthFlowTests.CodeOf(asOwner));
    }

    [Fact]
    public async Task Team_changes_are_audited()
    {
        var (owner, _) = await OwnerAsync();
        var ownerId = Guid.Parse(await UserIdAsync(owner));
        var (_, memberId) = await JoinAsync(owner, "member");
        await Patch(owner, memberId, "admin");
        await Delete(owner, $"/api/v1/team/members/{memberId}");

        var actions = (await fixture.AuditFor(ownerId)).Select(a => a.Action).ToList();
        Assert.Contains("team.invitation_created", actions);
        Assert.Contains("team.member_role_changed", actions);
        Assert.Contains("team.member_removed", actions);
        Assert.Contains("team.invitation_accepted", (await fixture.AuditFor(Guid.Parse(memberId))).Select(a => a.Action));
    }

    private async Task<(ApiClient Client, string WorkspaceId)> OwnerAsync(string business = "Almanaam")
    {
        var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), business)).Content.ReadFromJsonAsync<JsonElement>();
        return (client, me.GetProperty("workspace").GetProperty("id").GetString()!);
    }

    private async Task<(string Id, string Token, string Email)> InviteAsync(ApiClient inviter, string email, string role = "member")
    {
        var created = await (await inviter.PostAsync("/api/v1/team/invitations", new { email, role })).Content.ReadFromJsonAsync<JsonElement>();
        return (created.GetProperty("id").GetString()!, TokenFrom(Api.Emails.LastTo(email).TextBody), email);
    }

    /// <summary>Invites someone with <paramref name="role"/> and accepts; returns their signed-in client and user ID.</summary>
    private async Task<(ApiClient Client, string UserId)> JoinAsync(ApiClient owner, string role)
    {
        var invite = await InviteAsync(owner, AuthFixture.NewEmail(), role);
        var client = Api.CreateApiClient();
        var me = await (await client.PostAsync("/api/v1/invitations/accept", new { token = invite.Token, name = role, password = Password }))
            .Content.ReadFromJsonAsync<JsonElement>();
        return (client, me.GetProperty("user").GetProperty("id").GetString()!);
    }

    private static async Task<string> UserIdAsync(ApiClient client) =>
        (await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("user").GetProperty("id").GetString()!;

    private async Task<string?> PreviewCode(string token)
    {
        using var client = Api.CreateApiClient();
        var response = await client.PostAsync("/api/v1/invitations/preview", new { token });
        return response.IsSuccessStatusCode ? null : await AuthFlowTests.CodeOf(response);
    }

    private static async Task<HttpResponseMessage> Patch(ApiClient client, string userId, string role)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PatchAsJsonAsync($"/api/v1/team/members/{userId}", new { role });
    }

    private static async Task<HttpResponseMessage> Delete(ApiClient client, string path)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.DeleteAsync(path);
    }

    private static async Task AssertForbidden(Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("team.forbidden", await AuthFlowTests.CodeOf(response));
    }

    private static string TokenFrom(string body)
    {
        var match = InvitationLink().Match(body);
        Assert.True(match.Success, "No invitation link in the email.");
        return Uri.UnescapeDataString(match.Groups["token"].Value);
    }

    [GeneratedRegex(@"/accept-invitation\?token=(?<token>[A-Za-z0-9_\-%]+)")]
    private static partial Regex InvitationLink();
}
