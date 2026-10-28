using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure.Sessions;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features.Account;

/// <summary>The signed-in user and the server-side session of this request.</summary>
internal sealed class CurrentSession(IHttpContextAccessor http, IdentityModuleDbContext db, TimeProvider time)
{
    /// <summary>How long a sign-in or password re-check counts as "recent" (ADR 0015).</summary>
    public static readonly TimeSpan RecentAuthWindow = TimeSpan.FromMinutes(10);

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    public Guid UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public Guid SessionId => Guid.TryParse(User.FindFirstValue(SessionClaims.SessionId), out var id) ? id : Guid.Empty;

    public Task<UserSession?> GetAsync(CancellationToken ct)
    {
        var sessionId = SessionId;
        var userId = UserId;
        return db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
    }

    public bool IsRecent(UserSession session)
    {
        var now = time.GetUtcNow();
        return now - session.CreatedAt < RecentAuthWindow
            || (session.ReauthenticatedAt is { } at && now - at < RecentAuthWindow);
    }
}

/// <summary>Endpoint filter: the user signed in or re-entered their password in the last 10 minutes.</summary>
internal sealed class RequireRecentAuth : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var current = context.HttpContext.RequestServices.GetRequiredService<CurrentSession>();
        var session = await current.GetAsync(context.HttpContext.RequestAborted);
        return session is not null && current.IsRecent(session) ? await next(context) : AccountErrors.ReauthRequired.ToProblem();
    }
}

internal static class AccountErrors
{
    public static readonly Error InvalidMfaCode = Error.Unauthorized("auth.invalid_mfa_code", "The code is not valid.");

    public static readonly Error MfaChallengeExpired =
        Error.Unauthorized("auth.mfa_challenge_expired", "Sign in again to enter a new code.");

    public static readonly Error ReauthRequired =
        Error.Forbidden("auth.reauth_required", "Enter your password again to continue.");

    public static readonly Error MfaAlreadyEnabled = Error.Conflict("auth.mfa_already_enabled", "Two-step sign-in is already on.");

    public static readonly Error MfaNotEnabled = Error.Conflict("auth.mfa_not_enabled", "Two-step sign-in is off.");

    public static readonly Error SessionNotFound = Error.NotFound("auth.session_not_found", "Session not found.");
}
