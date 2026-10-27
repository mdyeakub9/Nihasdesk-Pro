using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nihasdesk.Infrastructure.Tenancy;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1: the tenant comes only from the authenticated session — never from headers,
/// query strings or bodies, even when a client sends another tenant's ID.
/// </summary>
public sealed class TenantResolutionTests
{
    private static readonly Guid SessionTenant = Guid.CreateVersion7();
    private static readonly Guid OtherTenant = Guid.CreateVersion7();

    [Fact]
    public async Task Tenant_comes_from_the_session_and_ignores_tenant_ids_in_the_request()
    {
        var context = Request(Authenticated(SessionTenant));
        context.Request.Headers["X-Tenant-Id"] = OtherTenant.ToString();
        context.Request.QueryString = new QueryString($"?tenantId={OtherTenant}&tenant_id={OtherTenant}");
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> { ["tenantId"] = OtherTenant.ToString() });

        var tenant = await ResolveAsync(context);

        Assert.Equal(SessionTenant, tenant.TenantId);
    }

    [Fact]
    public async Task Anonymous_requests_get_no_tenant_even_with_a_tenant_claim()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TenantClaims.TenantId, SessionTenant.ToString())]));
        var context = Request(anonymous);
        context.Request.Headers["X-Tenant-Id"] = OtherTenant.ToString();

        var tenant = await ResolveAsync(context);

        Assert.False(tenant.HasTenant);
    }

    [Fact]
    public async Task Sessions_without_a_valid_tenant_claim_get_no_tenant()
    {
        var withoutClaim = Request(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u")], "cookie")));
        var emptyClaim = Request(Authenticated(Guid.Empty));

        Assert.False((await ResolveAsync(withoutClaim)).HasTenant);
        Assert.False((await ResolveAsync(emptyClaim)).HasTenant);
    }

    private static ClaimsPrincipal Authenticated(Guid tenantId) =>
        new(new ClaimsIdentity([new Claim(TenantClaims.TenantId, tenantId.ToString())], authenticationType: "cookie"));

    private static DefaultHttpContext Request(ClaimsPrincipal user) => new() { User = user };

    private static async Task<TenantContext> ResolveAsync(HttpContext context)
    {
        var tenant = new TenantContext();
        await new TenantResolutionMiddleware(_ => Task.CompletedTask).InvokeAsync(context, tenant);
        return tenant;
    }
}
