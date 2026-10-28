using System.Net.Http.Json;
using System.Text.Json;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Auth;

/// <summary>SEC-47: security-relevant auth actions are recorded.</summary>
[Collection(AuthTests.Name)]
public sealed class AuditLogTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Auth_flow_writes_audit_events_in_order()
    {
        var email = AuthFixture.NewEmail();
        using var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, email)).Content.ReadFromJsonAsync<JsonElement>();
        var userId = me.GetProperty("user").GetProperty("id").GetGuid();

        await client.PostAsync("/api/v1/auth/logout");
        await client.LoginAsync(email, "wrong password but long");
        await client.LoginAsync(email);
        await client.PostAsync("/api/v1/auth/forgot-password", new { email });
        var (resetUser, token) = PasswordResetTests.ParseResetLink(Api.Emails.LastTo(email).TextBody);
        await client.PostAsync("/api/v1/auth/reset-password", new { userId = resetUser, token, newPassword = "a brand new long password" });

        var actions = (await fixture.AuditFor(userId)).Select(a => a.Action).ToList();
        Assert.Equal(
            [
                "auth.signup",
                "auth.email_verified",
                "auth.logout",
                "auth.login_failed",
                "auth.login_succeeded",
                "auth.password_reset_requested",
                "auth.password_reset",
            ],
            actions);
    }

    [Fact]
    public async Task Lockout_and_repeated_signup_are_recorded()
    {
        var email = AuthFixture.NewEmail();
        using var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, email)).Content.ReadFromJsonAsync<JsonElement>();
        var userId = me.GetProperty("user").GetProperty("id").GetGuid();

        await client.SignupAsync(email);
        for (var i = 0; i < 6; i++)
        {
            await client.LoginAsync(email, "wrong password but long");
        }

        var actions = (await fixture.AuditFor(userId)).Select(a => a.Action).ToList();
        Assert.Contains("auth.signup_existing_email", actions);
        Assert.Contains("auth.login_locked_out", actions);
    }
}
