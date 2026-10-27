using System.Net;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Auth;

/// <summary>SEC-07: strict per-IP limits on login with a Retry-After header.</summary>
[Collection(AuthTests.Name)]
public sealed class RateLimitTests(AuthFixture fixture)
{
    [Fact]
    public async Task Login_is_rate_limited_per_client()
    {
        await using var api = new NihasdeskApiFactory(fixture.Database, new Dictionary<string, string?>
        {
            ["RateLimits:login:PermitLimit"] = "3",
            ["RateLimits:login:Window"] = "00:10:00",
        });
        using var client = api.CreateApiClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(AuthFixture.NewEmail())).StatusCode);
        }

        var limited = await client.LoginAsync(AuthFixture.NewEmail());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }
}
