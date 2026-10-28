using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;

namespace Nihasdesk.Modules.Identity.Features.Account;

/// <summary>
/// <c>/api/v1/auth/login/mfa</c> (second sign-in step) and <c>/api/v1/account/*</c>: the signed-in
/// user's own MFA, re-authentication, sessions and password (ADR 0015).
/// </summary>
internal static class AccountEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/auth/login/mfa", async (MfaLoginRequest request, AccountHandler handler, CancellationToken ct) =>
            {
                var result = await handler.CompleteMfaLoginAsync(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value.Me) : result.Error.ToProblem();
            })
            .AllowAnonymous()
            .AddEndpointFilter<ValidationFilter<MfaLoginRequest>>()
            .RequireRateLimiting(RatePolicies.Login);

        var account = endpoints.MapGroup("/api/v1/account").RequireAuthorization();

        account.MapGet("/mfa", async (AccountHandler handler, CancellationToken ct) => Results.Ok(await handler.MfaStatusAsync(ct)));

        account.MapPost("/mfa/setup", async (AccountHandler handler, CancellationToken ct) => Respond(await handler.SetupMfaAsync(ct)));

        account.MapPost("/mfa/enable", async (MfaCodeRequest request, AccountHandler handler, CancellationToken ct) =>
                Respond(await handler.EnableMfaAsync(request, ct)))
            .AddEndpointFilter<ValidationFilter<MfaCodeRequest>>()
            .RequireRateLimiting(RatePolicies.Login);

        account.MapPost("/mfa/disable", async (AccountHandler handler, CancellationToken ct) => NoContent(await handler.DisableMfaAsync(ct)))
            .AddEndpointFilter<RequireRecentAuth>();

        account.MapPost("/mfa/recovery-codes", async (AccountHandler handler, CancellationToken ct) =>
                Respond(await handler.RegenerateRecoveryCodesAsync(ct)))
            .AddEndpointFilter<RequireRecentAuth>();

        account.MapPost("/reauth", async (ReauthRequest request, AccountHandler handler, CancellationToken ct) =>
                NoContent(await handler.ReauthenticateAsync(request, ct)))
            .AddEndpointFilter<ValidationFilter<ReauthRequest>>()
            .RequireRateLimiting(RatePolicies.Login);

        account.MapGet("/sessions", async (AccountHandler handler, CancellationToken ct) => Results.Ok(await handler.SessionsAsync(ct)));

        account.MapDelete("/sessions/{id:guid}", async (Guid id, AccountHandler handler, CancellationToken ct) =>
            NoContent(await handler.RevokeSessionAsync(id, ct)));

        account.MapPost("/sessions/revoke-others", async (AccountHandler handler, CancellationToken ct) =>
        {
            await handler.RevokeOtherSessionsAsync(ct);
            return Results.NoContent();
        });

        account.MapPost("/password", async (ChangePasswordRequest request, AccountHandler handler, CancellationToken ct) =>
                NoContent(await handler.ChangePasswordAsync(request, ct)))
            .AddEndpointFilter<ValidationFilter<ChangePasswordRequest>>()
            .RequireRateLimiting(RatePolicies.Login);
    }

    private static IResult Respond<T>(SharedKernel.Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();

    private static IResult NoContent(SharedKernel.Result result) =>
        result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
}
