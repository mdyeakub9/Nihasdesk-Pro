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
/// Runs after authentication.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirst(TenantClaims.TenantId)?.Value, out var tenantId)
            && tenantId != Guid.Empty)
        {
            tenantContext.SetTenant(tenantId);
        }

        return next(context);
    }
}
