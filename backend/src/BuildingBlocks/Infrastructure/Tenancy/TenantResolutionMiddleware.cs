using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Nihasdesk.Infrastructure.Tenancy;

public static class TenantClaims
{
    /// <summary>Claim in the server-side session holding the workspace the user signed in to.</summary>
    public const string TenantId = "tenant_id";
}

/// <summary>
/// Sets <see cref="TenantContext"/> from the authenticated session, and only from there
/// (CLAUDE.md rule 1): tenant IDs in headers, query strings or bodies are never read.
/// Anonymous endpoints (signup, login, …) never get a tenant from an existing session; they work
/// on no tenant data, and signing in may start a different one. Runs after authentication and routing.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        var anonymousEndpoint = context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        if (!anonymousEndpoint
            && context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirst(TenantClaims.TenantId)?.Value, out var tenantId)
            && tenantId != Guid.Empty)
        {
            tenantContext.SetTenant(tenantId);
        }

        return next(context);
    }
}
