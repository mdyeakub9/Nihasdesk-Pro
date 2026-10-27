using System.Net.Http.Json;
using System.Text.Json;

namespace Nihasdesk.Api.Tests.Auth;

/// <summary>End to end: a session sees only its own workspace, whatever tenant IDs the client sends.</summary>
[Collection(AuthTests.Name)]
public sealed class TenantSessionTests(AuthFixture fixture)
{
    [Fact]
    public async Task A_session_only_sees_its_own_workspace()
    {
        using var shopA = fixture.Api.CreateApiClient();
        using var shopB = fixture.Api.CreateApiClient();
        var meA = await (await shopA.SignupAndVerifyAsync(fixture.Api.Emails, AuthFixture.NewEmail(), "Shop A"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var meB = await (await shopB.SignupAndVerifyAsync(fixture.Api.Emails, AuthFixture.NewEmail(), "Shop B"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var workspaceA = meA.GetProperty("workspace").GetProperty("id").GetString();
        var workspaceB = meB.GetProperty("workspace").GetProperty("id").GetString();
        Assert.NotEqual(workspaceA, workspaceB);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/me?tenantId={workspaceB}");
        request.Headers.Add("X-Tenant-Id", workspaceB);
        var response = await shopA.Http.SendAsync(request);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(workspaceA, me.GetProperty("workspace").GetProperty("id").GetString());
        Assert.Equal("Shop A", me.GetProperty("workspace").GetProperty("name").GetString());
    }
}
