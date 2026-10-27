using System.Net;
using Nihasdesk.Modules.Identity;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Auth;

/// <summary>ADR 0010 lifetimes and SEC-14 cookie flags, with a controllable clock.</summary>
[Collection(AuthTests.Name)]
public sealed class SessionTests(AuthFixture fixture)
{
    // Tests in a collection run one at a time, so moving the shared clock is safe; each test signs in fresh.
    private NihasdeskApiFactory _api => fixture.Api;

    [Fact]
    public async Task Session_cookie_is_host_only_secure_httponly_lax_and_a_browser_session_by_default()
    {
        using var client = await VerifiedUserAsync();

        var login = await client.LoginAsync(_lastEmail);
        var cookie = SessionCookie(login);

        Assert.StartsWith($"{IdentityModule.SessionCookieName}=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Remember_me_sets_a_persistent_cookie()
    {
        using var client = await VerifiedUserAsync();

        var login = await client.LoginAsync(_lastEmail, rememberMe: true);

        Assert.Contains("expires=", SessionCookie(login), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Browser_session_ends_after_two_idle_hours()
    {
        using var client = await VerifiedUserAsync();
        (await client.LoginAsync(_lastEmail)).EnsureSuccessStatusCode();

        _api.Clock.Advance(TimeSpan.FromHours(1.5));
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/v1/me")).StatusCode);

        _api.Clock.Advance(TimeSpan.FromHours(1.5));
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/v1/me")).StatusCode);

        _api.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Browser_session_ends_after_twelve_hours_even_when_active()
    {
        using var client = await VerifiedUserAsync();
        (await client.LoginAsync(_lastEmail)).EnsureSuccessStatusCode();

        for (var i = 0; i < 11; i++)
        {
            _api.Clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/v1/me")).StatusCode);
        }

        _api.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Remembered_session_slides_for_14_days_and_ends_after_30()
    {
        using var client = await VerifiedUserAsync();
        (await client.LoginAsync(_lastEmail, rememberMe: true)).EnsureSuccessStatusCode();

        for (var i = 0; i < 2; i++)
        {
            _api.Clock.Advance(TimeSpan.FromDays(13));
            Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/v1/me")).StatusCode);
        }

        _api.Clock.Advance(TimeSpan.FromDays(4) + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Remembered_session_ends_after_14_idle_days()
    {
        using var client = await VerifiedUserAsync();
        (await client.LoginAsync(_lastEmail, rememberMe: true)).EnsureSuccessStatusCode();

        _api.Clock.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_on_the_server_even_if_the_cookie_is_replayed()
    {
        using var client = await VerifiedUserAsync();
        var cookie = SessionCookie(await client.LoginAsync(_lastEmail)).Split(';')[0];

        (await client.PostAsync("/api/v1/auth/logout")).EnsureSuccessStatusCode();

        using var replay = _api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.SendAsync(request)).StatusCode);
    }

    private string _lastEmail = string.Empty;

    private async Task<ApiClient> VerifiedUserAsync()
    {
        _lastEmail = AuthFixture.NewEmail();
        using (var signup = _api.CreateApiClient())
        {
            (await signup.SignupAndVerifyAsync(_api.Emails, _lastEmail)).EnsureSuccessStatusCode();
        }

        return _api.CreateApiClient();
    }

    private static string SessionCookie(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(IdentityModule.SessionCookieName + "=", StringComparison.Ordinal));
    }
}
