using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Features.Me;
using Microsoft.AspNetCore.Http;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Nihasdesk.Modules.Identity.Infrastructure.Mfa;
using Nihasdesk.Modules.Identity.Infrastructure.Sessions;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features.Login;

internal sealed record LoginRequest(string Email, string Password, bool RememberMe);

/// <summary>Either a session (<see cref="Me"/>) or, for MFA users, the request for a code.</summary>
internal sealed record LoginOutcome(MeResponse? Me)
{
    public bool MfaRequired => Me is null;
}

internal sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256);
        // Only an upper bound here: a wrong password must look the same as any other failure.
        RuleFor(r => r.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
    }
}

/// <summary>
/// Password sign-in with lockout (5 failures → 15 minutes). Unknown email, wrong password and
/// lockout all return the same error (SEC-26); an unknown email still costs one hash.
/// </summary>
internal sealed class LoginHandler(
    UserManager<User> users,
    SignInManager<User> signInManager,
    IPasswordHasher<User> hasher,
    SessionSignIn sessions,
    MfaChallenge mfa,
    IHttpContextAccessor http,
    AuditLog audit)
{
    private static readonly User TimingDummy = new();

    public async Task<Result<LoginOutcome>> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            hasher.HashPassword(TimingDummy, request.Password);
            await audit.WriteAsync(AuditActions.LoginFailed, userId: null, tenantId: null, cancellationToken);
            return AuthErrors.InvalidCredentials;
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsNotAllowed)
        {
            // Identity rejects unverified accounts before checking the password; check it ourselves
            // so "not verified" is only revealed to someone who knows the password.
            var passwordOk = await users.CheckPasswordAsync(user, request.Password);
            await audit.WriteAsync(AuditActions.LoginFailed, user.Id, tenantId: null, cancellationToken);
            return passwordOk ? AuthErrors.EmailNotVerified : AuthErrors.InvalidCredentials;
        }

        if (!result.Succeeded)
        {
            await audit.WriteAsync(result.IsLockedOut ? AuditActions.LoginLockedOut : AuditActions.LoginFailed, user.Id, tenantId: null, cancellationToken);
            return AuthErrors.InvalidCredentials;
        }

        if (user.TwoFactorEnabled)
        {
            mfa.Issue(http.HttpContext!.Response, user, request.RememberMe);
            return Result.Success(new LoginOutcome(null));
        }

        var signedIn = await sessions.SignInAsync(user, request.RememberMe, cancellationToken);
        if (signedIn.IsFailure)
        {
            return signedIn.Error;
        }

        await audit.WriteAsync(AuditActions.LoginSucceeded, user.Id, signedIn.Value.Workspace.Id, cancellationToken);
        return Result.Success(new LoginOutcome(signedIn.Value));
    }
}
