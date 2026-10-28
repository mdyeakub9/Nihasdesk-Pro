using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nihasdesk.TestSupport;
using Npgsql;

namespace Nihasdesk.Api.Tests.Auth;

/// <summary>ADR 0015: optional MFA, password re-auth for risky actions, session list, password change.</summary>
[Collection(AuthTests.Name)]
public sealed class MfaAndSessionTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    // Identity's authenticator check reads the system clock, so codes use it too.
    private static string CodeFor(string secret) => Totp.Code(secret, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Turning_MFA_on_returns_recovery_codes_and_shows_on_me()
    {
        var (client, _) = await SignedInAsync();

        var (secret, codes) = await EnableMfaAsync(client);

        Assert.Equal(10, codes.Count);
        Assert.Matches("^[A-Z2-7]+$", secret);
        var me = await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.True(me.GetProperty("user").GetProperty("mfaEnabled").GetBoolean());
        var status = await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/account/mfa");
        Assert.Equal(10, status.GetProperty("recoveryCodesLeft").GetInt32());
    }

    [Fact]
    public async Task Users_with_MFA_need_the_code_before_a_session_exists()
    {
        var (owner, email) = await SignedInAsync();
        var (secret, _) = await EnableMfaAsync(owner);
        using var device = Api.CreateApiClient();

        var login = await (await device.LoginAsync(email)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(login.GetProperty("mfaRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await device.Http.GetAsync("/api/v1/me")).StatusCode);

        var wrong = await device.PostAsync("/api/v1/auth/login/mfa", new { code = "000000" });
        Assert.Equal("auth.invalid_mfa_code", await AuthFlowTests.CodeOf(wrong));

        var right = await device.PostAsync("/api/v1/auth/login/mfa", new { code = CodeFor(secret) });
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await device.Http.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Recovery_codes_work_once_and_the_challenge_expires()
    {
        var (owner, email) = await SignedInAsync();
        var (_, codes) = await EnableMfaAsync(owner);
        using var device = Api.CreateApiClient();

        await device.LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await device.PostAsync("/api/v1/auth/login/mfa", new { recoveryCode = codes[0] })).StatusCode);

        using var again = Api.CreateApiClient();
        await again.LoginAsync(email);
        Assert.Equal("auth.invalid_mfa_code", await AuthFlowTests.CodeOf(await again.PostAsync("/api/v1/auth/login/mfa", new { recoveryCode = codes[0] })));

        using var slow = Api.CreateApiClient();
        await slow.LoginAsync(email);
        Api.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal("auth.mfa_challenge_expired", await AuthFlowTests.CodeOf(await slow.PostAsync("/api/v1/auth/login/mfa", new { recoveryCode = codes[1] })));

        using var none = Api.CreateApiClient();
        Assert.Equal("auth.mfa_challenge_expired", await AuthFlowTests.CodeOf(await none.PostAsync("/api/v1/auth/login/mfa", new { code = "123456" })));
    }

    [Fact]
    public async Task Wrong_MFA_codes_lock_the_account()
    {
        var (owner, email) = await SignedInAsync();
        var (secret, _) = await EnableMfaAsync(owner);
        using var attacker = Api.CreateApiClient();
        await attacker.LoginAsync(email);

        for (var i = 0; i < 5; i++)
        {
            await attacker.PostAsync("/api/v1/auth/login/mfa", new { code = "000000" });
        }

        var locked = await attacker.PostAsync("/api/v1/auth/login/mfa", new { code = CodeFor(secret) });
        Assert.Equal("auth.invalid_mfa_code", await AuthFlowTests.CodeOf(locked));
    }

    [Fact]
    public async Task Secret_is_stored_encrypted_and_recovery_codes_only_as_hashes()
    {
        var (client, _) = await SignedInAsync();
        var userId = await UserIdAsync(client);
        var (secret, codes) = await EnableMfaAsync(client);

        await using var connection = new NpgsqlConnection(fixture.Database.AppConnectionString);
        await connection.OpenAsync();
        await using var tokens = new NpgsqlCommand("SELECT value FROM identity.user_tokens WHERE user_id = @u AND name = 'AuthenticatorKey'", connection);
        tokens.Parameters.AddWithValue("u", userId);
        var stored = (string)(await tokens.ExecuteScalarAsync())!;
        Assert.DoesNotContain(secret, stored, StringComparison.Ordinal);

        await using var hashes = new NpgsqlCommand("SELECT code_hash FROM identity.recovery_codes WHERE user_id = @u", connection);
        hashes.Parameters.AddWithValue("u", userId);
        var storedHashes = new List<byte[]>();
        await using (var reader = await hashes.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                storedHashes.Add((byte[])reader[0]);
            }
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(codes[0].Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant()));
        Assert.Equal(10, storedHashes.Count);
        Assert.Contains(storedHashes, h => h.AsSpan().SequenceEqual(expected));
    }

    [Fact]
    public async Task Risky_actions_need_the_password_again_after_ten_minutes()
    {
        var (owner, email) = await SignedInAsync();
        await EnableMfaAsync(owner);

        Api.Clock.Advance(TimeSpan.FromMinutes(11));
        var tooLate = await owner.PostAsync("/api/v1/account/mfa/disable");
        Assert.Equal(HttpStatusCode.Forbidden, tooLate.StatusCode);
        Assert.Equal("auth.reauth_required", await AuthFlowTests.CodeOf(tooLate));

        Assert.Equal("auth.invalid_credentials", await AuthFlowTests.CodeOf(await owner.PostAsync("/api/v1/account/reauth", new { password = "wrong password but long" })));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/v1/account/reauth", new { password = ApiClient.DefaultPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/v1/account/mfa/disable")).StatusCode);

        using var device = Api.CreateApiClient();
        var login = await (await device.LoginAsync(email)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(login.TryGetProperty("mfaRequired", out _));
    }

    [Fact]
    public async Task Changing_a_role_or_removing_a_member_needs_a_recent_password()
    {
        var (owner, _) = await SignedInAsync();
        var invitee = AuthFixture.NewEmail();
        await owner.PostAsync("/api/v1/team/invitations", new { email = invitee, role = "member" });
        var token = Uri.UnescapeDataString(System.Text.RegularExpressions.Regex.Match(Api.Emails.LastTo(invitee).TextBody, @"token=([A-Za-z0-9_\-%]+)").Groups[1].Value);
        using var member = Api.CreateApiClient();
        var me = await (await member.PostAsync("/api/v1/invitations/accept", new { token, name = "M", password = ApiClient.DefaultPassword + " x" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var memberId = me.GetProperty("user").GetProperty("id").GetString();

        Api.Clock.Advance(TimeSpan.FromMinutes(11));
        await owner.RefreshCsrfAsync();
        var patch = await owner.Http.PatchAsJsonAsync($"/api/v1/team/members/{memberId}", new { role = "admin" });
        Assert.Equal("auth.reauth_required", await AuthFlowTests.CodeOf(patch));
        // Invitations stay possible without it (ADR 0015).
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsync("/api/v1/team/invitations", new { email = AuthFixture.NewEmail(), role = "member" })).StatusCode);

        await owner.PostAsync("/api/v1/account/reauth", new { password = ApiClient.DefaultPassword });
        await owner.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.PatchAsJsonAsync($"/api/v1/team/members/{memberId}", new { role = "admin" })).StatusCode);
    }

    [Fact]
    public async Task Users_see_their_sessions_and_can_sign_out_other_devices()
    {
        var (laptop, email) = await SignedInAsync();
        using var phone = Api.CreateApiClient();
        (await phone.LoginAsync(email)).EnsureSuccessStatusCode();

        var sessions = await laptop.Http.GetFromJsonAsync<JsonElement>("/api/v1/account/sessions");
        Assert.True(sessions.GetArrayLength() >= 2);
        Assert.Single(sessions.EnumerateArray(), s => s.GetProperty("current").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await laptop.PostAsync("/api/v1/account/sessions/revoke-others")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.Http.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.Http.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Nobody_can_revoke_someone_elses_session()
    {
        var (victim, _) = await SignedInAsync();
        var victimSession = (await victim.Http.GetFromJsonAsync<JsonElement>("/api/v1/account/sessions"))[0].GetProperty("id").GetString();
        var (attacker, _) = await SignedInAsync();

        await attacker.RefreshCsrfAsync();
        var response = await attacker.Http.DeleteAsync($"/api/v1/account/sessions/{victimSession}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await victim.Http.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_signs_out_other_devices()
    {
        var (laptop, email) = await SignedInAsync();
        using var phone = Api.CreateApiClient();
        (await phone.LoginAsync(email)).EnsureSuccessStatusCode();
        const string newPassword = "una password nuova e lunga";

        Assert.Equal("auth.invalid_credentials", await AuthFlowTests.CodeOf(
            await laptop.PostAsync("/api/v1/account/password", new { currentPassword = "wrong password but long", newPassword })));
        Assert.Equal(HttpStatusCode.NoContent, (await laptop.PostAsync("/api/v1/account/password", new { currentPassword = ApiClient.DefaultPassword, newPassword })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.Http.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.Http.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal("Your password was changed", Api.Emails.LastTo(email).Subject);
        using var fresh = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.OK, (await fresh.LoginAsync(email, newPassword)).StatusCode);
    }

    private async Task<(ApiClient Client, string Email)> SignedInAsync()
    {
        var email = AuthFixture.NewEmail();
        var client = Api.CreateApiClient();
        (await client.SignupAndVerifyAsync(Api.Emails, email)).EnsureSuccessStatusCode();
        return (client, email);
    }

    private static async Task<Guid> UserIdAsync(ApiClient client) =>
        (await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("user").GetProperty("id").GetGuid();

    private static async Task<(string Secret, List<string> Codes)> EnableMfaAsync(ApiClient client)
    {
        var setup = await (await client.PostAsync("/api/v1/account/mfa/setup")).Content.ReadFromJsonAsync<JsonElement>();
        var secret = setup.GetProperty("secret").GetString()!;
        Assert.StartsWith("otpauth://totp/", setup.GetProperty("otpauthUri").GetString(), StringComparison.Ordinal);
        var enabled = await client.PostAsync("/api/v1/account/mfa/enable", new { code = CodeFor(secret) });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var codes = (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes")
            .EnumerateArray().Select(c => c.GetString()!).ToList();
        return (secret, codes);
    }
}
