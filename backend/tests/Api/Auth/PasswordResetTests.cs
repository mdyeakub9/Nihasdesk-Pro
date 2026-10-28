using System.Net;
using System.Text.RegularExpressions;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Auth;

[Collection(AuthTests.Name)]
public sealed partial class PasswordResetTests(AuthFixture fixture)
{
    private const string NewPassword = "a brand new long password";

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Reset_sets_the_new_password_revokes_all_sessions_and_notifies()
    {
        var email = AuthFixture.NewEmail();
        using var laptop = Api.CreateApiClient();
        (await laptop.SignupAndVerifyAsync(Api.Emails, email)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await laptop.Http.GetAsync("/api/v1/me")).StatusCode);

        using var phone = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Accepted, (await phone.PostAsync("/api/v1/auth/forgot-password", new { email })).StatusCode);
        var (userId, token) = ParseResetLink(Api.Emails.LastTo(email).TextBody);

        var reset = await phone.PostAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.Http.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal("Your password was changed", Api.Emails.LastTo(email).Subject);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.LoginAsync(email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phone.LoginAsync(email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_link_works_only_once()
    {
        var email = AuthFixture.NewEmail();
        using var client = Api.CreateApiClient();
        (await client.SignupAndVerifyAsync(Api.Emails, email)).EnsureSuccessStatusCode();
        await client.PostAsync("/api/v1/auth/forgot-password", new { email });
        var (userId, token) = ParseResetLink(Api.Emails.LastTo(email).TextBody);

        (await client.PostAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = NewPassword })).EnsureSuccessStatusCode();
        var again = await client.PostAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = "yet another long password" });

        Assert.Equal("auth.invalid_token", await AuthFlowTests.CodeOf(again));
    }

    [Fact]
    public async Task Reset_link_expires()
    {
        // Identity's token provider reads the system clock, so this uses a 1-second lifespan
        // instead of moving the test clock; production is fixed at 1 hour (SEC-26).
        await using var api = new NihasdeskApiFactory(fixture.Database, new Dictionary<string, string?>
        {
            ["Identity:TokenLifespan"] = "00:00:01",
        });
        var email = AuthFixture.NewEmail();
        using var client = api.CreateApiClient();
        (await client.SignupAndVerifyAsync(api.Emails, email)).EnsureSuccessStatusCode();
        await client.PostAsync("/api/v1/auth/forgot-password", new { email });
        var (userId, token) = ParseResetLink(api.Emails.LastTo(email).TextBody);

        await Task.Delay(TimeSpan.FromSeconds(2));
        var late = await client.PostAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = NewPassword });

        Assert.Equal("auth.invalid_token", await AuthFlowTests.CodeOf(late));
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_and_mails_only_verified_accounts()
    {
        var verified = AuthFixture.NewEmail();
        var unverified = AuthFixture.NewEmail();
        using var client = Api.CreateApiClient();
        (await client.SignupAndVerifyAsync(Api.Emails, verified)).EnsureSuccessStatusCode();
        (await client.SignupAsync(unverified)).EnsureSuccessStatusCode();
        var unverifiedMails = Api.Emails.Sent.Count(m => m.To == unverified);

        var responses = new[]
        {
            await client.PostAsync("/api/v1/auth/forgot-password", new { email = verified }),
            await client.PostAsync("/api/v1/auth/forgot-password", new { email = unverified }),
            await client.PostAsync("/api/v1/auth/forgot-password", new { email = AuthFixture.NewEmail() }),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Equal("Reset your password", Api.Emails.LastTo(verified).Subject);
        Assert.Equal(unverifiedMails, Api.Emails.Sent.Count(m => m.To == unverified));
    }

    [Fact]
    public async Task Reset_unlocks_a_locked_account()
    {
        var email = AuthFixture.NewEmail();
        using var client = Api.CreateApiClient();
        (await client.SignupAndVerifyAsync(Api.Emails, email)).EnsureSuccessStatusCode();
        for (var i = 0; i < 5; i++)
        {
            await client.LoginAsync(email, "wrong password but long");
        }

        await client.PostAsync("/api/v1/auth/forgot-password", new { email });
        var (userId, token) = ParseResetLink(Api.Emails.LastTo(email).TextBody);
        (await client.PostAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = NewPassword })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, NewPassword)).StatusCode);
    }

    internal static (Guid UserId, string Token) ParseResetLink(string body)
    {
        var match = ResetLink().Match(body);
        Assert.True(match.Success, "No reset link in the email.");
        return (Guid.Parse(match.Groups["user"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    [GeneratedRegex(@"/reset-password\?user=(?<user>[0-9a-f-]{36})&token=(?<token>[^\s""]+)")]
    private static partial Regex ResetLink();
}
