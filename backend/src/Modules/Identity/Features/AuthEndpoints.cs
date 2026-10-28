using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Identity.Features.ForgotPassword;
using Nihasdesk.Modules.Identity.Features.Login;
using Nihasdesk.Modules.Identity.Features.Me;
using Nihasdesk.Modules.Identity.Features.ResendVerification;
using Nihasdesk.Modules.Identity.Features.ResetPassword;
using Nihasdesk.Modules.Identity.Features.Signup;
using Nihasdesk.Modules.Identity.Features.VerifyEmail;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;

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
        auth.MapPost("/logout", async (HttpContext context, AuditLog audit, CancellationToken ct) =>
        {
            if (context.User.Identity?.IsAuthenticated == true
                && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                Guid? tenantId = Guid.TryParse(context.User.FindFirstValue(TenantClaims.TenantId), out var t) ? t : null;
                await audit.WriteAsync(AuditActions.Logout, userId, tenantId, ct);
            }

            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Results.NoContent();
        });

        auth.MapPost("/forgot-password", async (ForgotPasswordRequest request, ForgotPasswordHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(request, ct);
                return Results.Accepted();
            })
            .AddEndpointFilter<ValidationFilter<ForgotPasswordRequest>>()
            .RequireRateLimiting(RatePolicies.Email);

        auth.MapPost("/reset-password", async (ResetPasswordRequest request, ResetPasswordHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<ResetPasswordRequest>>()
            .RequireRateLimiting(RatePolicies.Email);

        endpoints.MapGet("/api/v1/me", async (ClaimsPrincipal user, MeHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(user, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .RequireAuthorization();
    }
}
