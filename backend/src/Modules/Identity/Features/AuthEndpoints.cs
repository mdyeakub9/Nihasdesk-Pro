using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Modules.Identity.Features.Login;
using Nihasdesk.Modules.Identity.Features.Me;
using Nihasdesk.Modules.Identity.Features.ResendVerification;
using Nihasdesk.Modules.Identity.Features.Signup;
using Nihasdesk.Modules.Identity.Features.VerifyEmail;

namespace Nihasdesk.Modules.Identity.Features;

/// <summary>
/// <c>/api/v1/auth/*</c> (anonymous, rate-limited, CSRF-protected by middleware) and <c>/api/v1/me</c>.
/// </summary>
internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/v1/auth").AllowAnonymous();

        auth.MapPost("/signup", async (SignupRequest request, SignupHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, ct);
                return result.IsSuccess ? Results.Accepted() : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<SignupRequest>>()
            .RequireRateLimiting(RatePolicies.Signup);

        auth.MapPost("/verify-email", async (VerifyEmailRequest request, VerifyEmailHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<VerifyEmailRequest>>()
            .RequireRateLimiting(RatePolicies.Email);

        auth.MapPost("/resend-verification", async (ResendVerificationRequest request, ResendVerificationHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(request, ct);
                return Results.Accepted();
            })
            .AddEndpointFilter<ValidationFilter<ResendVerificationRequest>>()
            .RequireRateLimiting(RatePolicies.Email);

        auth.MapPost("/login", async (LoginRequest request, LoginHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .RequireRateLimiting(RatePolicies.Login);

        // Deleting the cookie also deletes the server-side session (DatabaseTicketStore.RemoveAsync).
        auth.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Results.NoContent();
        });

        endpoints.MapGet("/api/v1/me", async (ClaimsPrincipal user, MeHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(user, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .RequireAuthorization();
    }
}
