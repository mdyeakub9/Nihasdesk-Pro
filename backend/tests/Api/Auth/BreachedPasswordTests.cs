using System.Net;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Auth;

/// <summary>ADR 0009: HIBP k-anonymity check, fail-open, and no enumeration through it.</summary>
[Collection(AuthTests.Name)]
public sealed class BreachedPasswordTests(AuthFixture fixture)
{
    private const string Breached = "password1234 is very common";

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Breached_password_is_rejected_at_signup()
    {
        Api.Hibp.MarkBreached(Breached);
        using var client = Api.CreateApiClient();

        var response = await client.SignupAsync(AuthFixture.NewEmail(), Breached);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("auth.breached_password", await AuthFlowTests.CodeOf(response));
    }

    [Fact]
    public async Task Breached_password_gets_the_same_answer_for_registered_and_new_emails()
    {
        Api.Hibp.MarkBreached(Breached);
        var registered = AuthFixture.NewEmail();
        using var client = Api.CreateApiClient();
        (await client.SignupAsync(registered)).EnsureSuccessStatusCode();

        var forRegistered = await client.SignupAsync(registered, Breached);
        var forNew = await client.SignupAsync(AuthFixture.NewEmail(), Breached);

        Assert.Equal(forNew.StatusCode, forRegistered.StatusCode);
        Assert.Equal(await AuthFlowTests.CodeOf(forNew), await AuthFlowTests.CodeOf(forRegistered));
    }

    [Fact]
    public async Task Breached_password_is_rejected_at_reset()
    {
        Api.Hibp.MarkBreached(Breached);
        var email = AuthFixture.NewEmail();
        using var client = Api.CreateApiClient();
        (await client.SignupAndVerifyAsync(Api.Emails, email)).EnsureSuccessStatusCode();
        await client.PostAsync("/api/v1/auth/forgot-password", new { email });
        var (userId, token) = PasswordResetTests.ParseResetLink(Api.Emails.LastTo(email).TextBody);

        var response = await client.PostAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = Breached });

        Assert.Equal("auth.breached_password", await AuthFlowTests.CodeOf(response));
    }

    [Fact]
    public async Task Only_a_five_character_hash_prefix_is_sent()
    {
        var before = Api.Hibp.Requests.Count;
        using var client = Api.CreateApiClient();

        await client.SignupAsync(AuthFixture.NewEmail());

        var request = Api.Hibp.Requests.Skip(before).First();
        var path = request.RequestUri!.AbsolutePath;
        Assert.Matches("^/range/[0-9A-F]{5}$", path);
        Assert.Equal("true", request.Headers.GetValues("Add-Padding").Single());
        Assert.DoesNotContain(ApiClient.DefaultPassword, request.RequestUri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Password_is_accepted_when_the_service_is_down()
    {
        Api.Hibp.Unavailable = true;
        try
        {
            using var client = Api.CreateApiClient();

            var response = await client.SignupAsync(AuthFixture.NewEmail());

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        finally
        {
            Api.Hibp.Unavailable = false;
        }
    }
}
