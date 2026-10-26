namespace Nihasdesk.Api.Middleware;

/// <summary>
/// Security headers for every API response (SEC-30). The API serves JSON only, so the CSP
/// allows nothing and the response can't be framed.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // TLS ends at the reverse proxy (SEC-31); browsers ignore HSTS on plain HTTP.
            if (!environment.IsDevelopment())
            {
                headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
