using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Email.Outbox;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Features.Login;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Nihasdesk.Modules.Identity.Infrastructure.Email;
using Nihasdesk.Modules.Identity.Infrastructure.Mfa;
using Nihasdesk.Modules.Identity.Infrastructure.Sessions;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features.Account;

/// <summary>The signed-in user's own security settings (ADR 0015): MFA, re-auth, sessions, password.</summary>
internal sealed class AccountHandler(
    UserManager<User> users,
    IdentityModuleDbContext db,
    CurrentSession current,
    SessionSignIn sessions,
    MfaChallenge challenge,
    IHttpContextAccessor http,
    IEmailOutbox email,
    DashboardLinks links,
    AuditLog audit,
    TimeProvider time)
{
    private const int RecoveryCodeCount = 10;

    /// <summary>Second sign-in step for MFA users: authenticator code or one recovery code.</summary>
    public async Task<Result<LoginOutcome>> CompleteMfaLoginAsync(MfaLoginRequest request, CancellationToken ct)
    {
        var context = http.HttpContext!;
        var pending = challenge.Read(context.Request);
        var user = pending is null ? null : await users.FindByIdAsync(pending.UserId.ToString());
        if (pending is null || user is null || user.SecurityStamp != pending.SecurityStamp || !user.TwoFactorEnabled)
        {
            return AccountErrors.MfaChallengeExpired;
        }

        if (await users.IsLockedOutAsync(user))
        {
            return AccountErrors.InvalidMfaCode;
        }

        var valid = !string.IsNullOrWhiteSpace(request.Code)
            ? await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code.Replace(" ", string.Empty, StringComparison.Ordinal))
            : (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.RecoveryCode!)).Succeeded;

        if (!valid)
        {
            // Wrong codes count toward the normal lockout (SEC-26).
            await users.AccessFailedAsync(user);
            await audit.WriteAsync(AuditActions.MfaFailed, user.Id, tenantId: null, ct);
            return AccountErrors.InvalidMfaCode;
        }

        await users.ResetAccessFailedCountAsync(user);
        MfaChallenge.Clear(context.Response);
        var signedIn = await sessions.SignInAsync(user, pending.Remember, ct);
        if (signedIn.IsFailure)
        {
            return signedIn.Error;
        }

        await audit.WriteAsync(AuditActions.LoginSucceeded, user.Id, signedIn.Value.Workspace.Id, ct);
        return Result.Success(new LoginOutcome(signedIn.Value));
    }

    public async Task<MfaStatusResponse> MfaStatusAsync(CancellationToken ct)
    {
        var user = await UserAsync();
        return new MfaStatusResponse(user.TwoFactorEnabled, user.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(user) : 0);
    }

    /// <summary>New secret for the authenticator app; MFA turns on only after a valid code.</summary>
    public async Task<Result<MfaSetupResponse>> SetupMfaAsync(CancellationToken ct)
    {
        var user = await UserAsync();
        if (user.TwoFactorEnabled)
        {
            return AccountErrors.MfaAlreadyEnabled;
        }

        await users.ResetAuthenticatorKeyAsync(user);
        var secret = (await users.GetAuthenticatorKeyAsync(user))!;
        var label = UrlEncoder.Default.Encode($"Nihasdesk:{user.Email}");
        var uri = $"otpauth://totp/{label}?secret={secret}&issuer=Nihasdesk&digits=6";
        return Result.Success(new MfaSetupResponse(secret, uri));
    }

    public async Task<Result<RecoveryCodesResponse>> EnableMfaAsync(MfaCodeRequest request, CancellationToken ct)
    {
        var user = await UserAsync();
        if (user.TwoFactorEnabled)
        {
            return AccountErrors.MfaAlreadyEnabled;
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code.Replace(" ", string.Empty, StringComparison.Ordinal)))
        {
            return AccountErrors.InvalidMfaCode;
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToList();
        await audit.WriteAsync(AuditActions.MfaEnabled, user.Id, tenantId: null, ct);
        return Result.Success(new RecoveryCodesResponse(codes));
    }

    public async Task<Result> DisableMfaAsync(CancellationToken ct)
    {
        var user = await UserAsync();
        if (!user.TwoFactorEnabled)
        {
            return Result.Failure(AccountErrors.MfaNotEnabled);
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        // A fresh secret makes the old authenticator entry useless; no recovery codes remain.
        await users.ResetAuthenticatorKeyAsync(user);
        await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        await audit.WriteAsync(AuditActions.MfaDisabled, user.Id, tenantId: null, ct);
        return Result.Success();
    }

    public async Task<Result<RecoveryCodesResponse>> RegenerateRecoveryCodesAsync(CancellationToken ct)
    {
        var user = await UserAsync();
        if (!user.TwoFactorEnabled)
        {
            return AccountErrors.MfaNotEnabled;
        }

        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToList();
        await audit.WriteAsync(AuditActions.RecoveryCodesRegenerated, user.Id, tenantId: null, ct);
        return Result.Success(new RecoveryCodesResponse(codes));
    }

    /// <summary>Password re-check; afterwards risky actions are allowed for 10 minutes in this session.</summary>
    public async Task<Result> ReauthenticateAsync(ReauthRequest request, CancellationToken ct)
    {
        var user = await UserAsync();
        if (await users.IsLockedOutAsync(user) || !await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            return Result.Failure(AuthErrors.InvalidCredentials);
        }

        await users.ResetAccessFailedCountAsync(user);
        var session = await current.GetAsync(ct);
        if (session is null)
        {
            return Result.Failure(AccountErrors.SessionNotFound);
        }

        session.ReauthenticatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.Reauthenticated, user.Id, session.TenantId, ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<SessionResponse>> SessionsAsync(CancellationToken ct)
    {
        var userId = current.UserId;
        var currentId = current.SessionId;
        var list = await db.Sessions.Where(s => s.UserId == userId).OrderByDescending(s => s.LastSeenAt).ToListAsync(ct);
        return list.Select(s => new SessionResponse(s.Id, s.UserAgent, s.Remember, s.CreatedAt, s.LastSeenAt, s.Id == currentId)).ToList();
    }

    public async Task<Result> RevokeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var userId = current.UserId;
        var deleted = await db.Sessions.Where(s => s.Id == sessionId && s.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0)
        {
            return Result.Failure(AccountErrors.SessionNotFound);
        }

        await audit.WriteAsync(AuditActions.SessionRevoked, userId, tenantId: null, ct);
        return Result.Success();
    }

    public async Task RevokeOtherSessionsAsync(CancellationToken ct)
    {
        var userId = current.UserId;
        var currentId = current.SessionId;
        await db.Sessions.Where(s => s.UserId == userId && s.Id != currentId).ExecuteDeleteAsync(ct);
        await audit.WriteAsync(AuditActions.SessionRevoked, userId, tenantId: null, ct);
    }

    /// <summary>Needs the current password; signs out every other session and sends a notice.</summary>
    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await UserAsync();
        if (await users.IsLockedOutAsync(user))
        {
            return Result.Failure(AuthErrors.InvalidCredentials);
        }

        var changed = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            if (changed.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                await users.AccessFailedAsync(user);
                return Result.Failure(AuthErrors.InvalidCredentials);
            }

            return Result.Failure(AuthErrors.ForPassword(changed.Errors));
        }

        await RevokeOtherSessionsAsync(ct);
        await email.EnqueueAsync(AuthEmails.PasswordChanged(user.Email!, user.Language, user.DisplayName, links.Login), ct);
        await audit.WriteAsync(AuditActions.PasswordChanged, user.Id, tenantId: null, ct);
        return Result.Success();
    }

    private async Task<User> UserAsync() =>
        await users.FindByIdAsync(current.UserId.ToString()) ?? throw new InvalidOperationException("Signed-in user not found.");
}
