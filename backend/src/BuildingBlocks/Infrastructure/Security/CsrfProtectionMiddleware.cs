using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Nihasdesk.Infrastructure.Security;

/// <summary>
/// Every state-changing API request must carry a valid antiforgery token in
/// <c>X-XSRF-TOKEN</c> (SEC-03). The token is bound to the current user, so the client fetches
/// a new one from <c>GET /api/v1/csrf</c> after signing in or out. Runs after authentication.
/// Endpoints marked <c>DisableAntiforgery()</c> are skipped: only server-to-server webhooks, which
/// carry no cookies and are authenticated by their signature instead (SEC-17).
/// </summary>
public sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(antiforgery);

        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method)
            && context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>() is not { RequiresValidation: false }
            && !await antiforgery.IsRequestValidAsync(context))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing or invalid CSRF token.",
                extensions: new Dictionary<string, object?> { ["code"] = "csrf.invalid" })
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
