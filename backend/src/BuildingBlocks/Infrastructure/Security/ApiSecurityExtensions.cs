using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Nihasdesk.Infrastructure.Security;

/// <summary>Named rate-limit policies (SEC-07), configurable under <c>RateLimits</c>. Per client IP, except <see cref="Messages"/>, <see cref="Uploads"/> and <see cref="ProductImages"/>.</summary>
public static class RatePolicies
{
    public const string Login = "login";

    public const string Signup = "signup";

    /// <summary>Endpoints that send email: resend verification, later password reset and invitations.</summary>
    public const string Email = "email";

    /// <summary>Team invitations (create, resend).</summary>
    public const string Invite = "invite";

    /// <summary>Team replies and notes in the inbox; per signed-in user.</summary>
    public const string Messages = "messages";

    /// <summary>File uploads (CSV import, later documents and images); per signed-in user (SEC-07).</summary>
    public const string Uploads = "uploads";

    /// <summary>Product photos in the catalog editor: up to 8 per product, so more than other uploads; per signed-in user.</summary>
    public const string ProductImages = "product-images";

    /// <summary>Signed public product image links, fetched by Meta and customers' apps; per client IP.</summary>
    public const string PublicMedia = "public-media";
}

public sealed class RateLimitSettings
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}

public static class ApiSecurityExtensions
{
    public const string CsrfHeaderName = "X-XSRF-TOKEN";

    private static readonly Dictionary<string, RateLimitSettings> Defaults = new()
    {
        [RatePolicies.Login] = new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) },
        [RatePolicies.Signup] = new() { PermitLimit = 5, Window = TimeSpan.FromMinutes(15) },
        [RatePolicies.Email] = new() { PermitLimit = 5, Window = TimeSpan.FromMinutes(15) },
        [RatePolicies.Invite] = new() { PermitLimit = 30, Window = TimeSpan.FromHours(1) },
        [RatePolicies.Messages] = new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) },
        [RatePolicies.Uploads] = new() { PermitLimit = 30, Window = TimeSpan.FromHours(1) },
        [RatePolicies.ProductImages] = new() { PermitLimit = 300, Window = TimeSpan.FromHours(1) },
        [RatePolicies.PublicMedia] = new() { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) },
    };

    /// <summary>Antiforgery for cookie-authenticated APIs (SEC-03) and auth rate limits (SEC-07).</summary>
    public static IServiceCollection AddApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeaderName;
            options.Cookie.Name = "__Host-nd_xsrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            foreach (var (name, fallback) in Defaults)
            {
                var settings = configuration.GetSection($"RateLimits:{name}").Get<RateLimitSettings>() ?? fallback;
                options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(name, context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = settings.Window,
                        QueueLimit = 0,
                    }));
            }
        });

        return services;
    }

    private static string PartitionKey(string policy, HttpContext context) =>
        policy is RatePolicies.Messages or RatePolicies.Uploads or RatePolicies.ProductImages && context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
            ? "user:" + userId
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary><c>GET /api/v1/csrf</c>: issues the antiforgery cookie and returns the token for the header.</summary>
    public static IEndpointConventionBuilder MapCsrfEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new CsrfResponse(tokens.RequestToken!));
        }).AllowAnonymous();
}

public sealed record CsrfResponse(string Token);
