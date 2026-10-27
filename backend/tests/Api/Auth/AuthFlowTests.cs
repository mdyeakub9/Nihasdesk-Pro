using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Auth;

[Collection(AuthTests.Name)]
public sealed class AuthFlowTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Signup_verify_me_logout_login_round_trip()
    {
        using var client = Api.CreateApiClient();
        var email = AuthFixture.NewEmail();

        var signup = await client.SignupAsync(email, business: "Bottega Verde");
        Assert.Equal(HttpStatusCode.Accepted, signup.StatusCode);
        Assert.Equal("Confirm your email address", Api.Emails.LastTo(email).Subject);

        var (userId, token) = ApiClient.ParseVerifyLink(Api.Emails.LastTo(email).TextBody);
        var verified = await client.PostAsync("/api/v1/auth/verify-email", new { userId, token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        var me = await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(email, me.GetProperty("user").GetProperty("email").GetString());
        Assert.True(me.GetProperty("user").GetProperty("emailVerified").GetBoolean());
        Assert.Equal("Bottega Verde", me.GetProperty("workspace").GetProperty("name").GetString());
        Assert.Equal("owner", me.GetProperty("role").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/v1/me")).StatusCode);

        var login = await client.LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Signup_with_a_registered_email_answers_the_same_and_sends_a_notice()
    {
        using var client = Api.CreateApiClient();
        var email = AuthFixture.NewEmail();

        var first = await client.SignupAsync(email);
        var second = await client.SignupAsync(email, password: "another long password 123");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal("You already have a Nihasdesk account", Api.Emails.LastTo(email).Subject);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_get_the_same_error()
    {
        using var client = Api.CreateApiClient();
        var email = AuthFixture.NewEmail();
        (await client.SignupAndVerifyAsync(Api.Emails, email)).EnsureSuccessStatusCode();
        using var other = Api.CreateApiClient();

        var unknown = await other.LoginAsync(AuthFixture.NewEmail());
        var wrong = await other.LoginAsync(email, "wrong password but long");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("auth.invalid_credentials", await CodeOf(unknown));
        Assert.Equal("auth.invalid_credentials", await CodeOf(wrong));
    }

    [Fact]
    public async Task Unverified_account_is_only_revealed_with_the_right_password()
    {
        using var client = Api.CreateApiClient();
        var email = AuthFixture.NewEmail();
        (await client.SignupAsync(email)).EnsureSuccessStatusCode();

        var wrong = await client.LoginAsync(email, "wrong password but long");
        var right = await client.LoginAsync(email);

        Assert.Equal("auth.invalid_credentials", await CodeOf(wrong));
        Assert.Equal(HttpStatusCode.Forbidden, right.StatusCode);
        Assert.Equal("auth.email_not_verified", await CodeOf(right));
    }

    [Fact]
    public async Task Account_locks_after_five_failed_logins_with_the_same_error()
    {
        using var client = Api.CreateApiClient();
        var email = AuthFixture.NewEmail();
        (await client.SignupAndVerifyAsync(Api.Emails, email)).EnsureSuccessStatusCode();
        using var attacker = Api.CreateApiClient();

        for (var i = 0; i < 5; i++)
        {
            await attacker.LoginAsync(email, "wrong password but long");
        }

        var locked = await attacker.LoginAsync(email);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal("auth.invalid_credentials", await CodeOf(locked));
    }

    [Fact]
    public async Task Verification_link_works_once_and_bad_tokens_are_rejected()
    {
        using var client = Api.CreateApiClient();
        var email = AuthFixture.NewEmail();
        (await client.SignupAsync(email)).EnsureSuccessStatusCode();
        var (userId, token) = ApiClient.ParseVerifyLink(Api.Emails.LastTo(email).TextBody);

        var forged = await client.PostAsync("/api/v1/auth/verify-email", new { userId, token = token + "x" });
        Assert.Equal("auth.invalid_token", await CodeOf(forged));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/verify-email", new { userId, token })).StatusCode);
    }

    [Fact]
    public async Task Resend_verification_answers_the_same_for_unknown_and_known_emails()
    {
        using var client = Api.CreateApiClient();
        var email = AuthFixture.NewEmail();
        (await client.SignupAsync(email)).EnsureSuccessStatusCode();
        var before = Api.Emails.Sent.Count(m => m.To == email);

        var known = await client.PostAsync("/api/v1/auth/resend-verification", new { email });
        var unknown = await client.PostAsync("/api/v1/auth/resend-verification", new { email = AuthFixture.NewEmail() });

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(before + 1, Api.Emails.Sent.Count(m => m.To == email));
    }

    [Fact]
    public async Task Signup_validates_input_with_problem_details()
    {
        using var client = Api.CreateApiClient();

        var response = await client.SignupAsync("not-an-email", password: "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation.failed", await CodeOf(response));
    }

    [Fact]
    public async Task State_changing_requests_without_a_csrf_token_are_rejected()
    {
        using var client = Api.CreateApiClient();

        var response = await client.Http.PostAsJsonAsync("/api/v1/auth/login", new { email = "a@example.com", password = "x", rememberMe = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("csrf.invalid", await CodeOf(response));
    }

    [Fact]
    public async Task Me_without_a_session_is_unauthorized()
    {
        using var client = Api.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/v1/me")).StatusCode);
    }

    internal static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
