using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Features.Me;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Nihasdesk.Modules.Identity.Infrastructure.Sessions;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features.VerifyEmail;

internal sealed record VerifyEmailRequest(Guid UserId, string Token);

internal sealed class VerifyEmailValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.Token).NotEmpty().MaximumLength(2048);
    }
}

/// <summary>Confirms the email from the link and signs the user in (browser session, ADR 0010).</summary>
internal sealed class VerifyEmailHandler(UserManager<User> users, SessionSignIn sessions, AuditLog audit)
{
    public async Task<Result<MeResponse>> HandleAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return AuthErrors.InvalidToken;
        }

        var confirmed = await users.ConfirmEmailAsync(user, request.Token);
        if (!confirmed.Succeeded)
        {
            return AuthErrors.InvalidToken;
        }

        var signedIn = await sessions.SignInAsync(user, remember: false, cancellationToken);
        await audit.WriteAsync(AuditActions.EmailVerified, user.Id, signedIn.IsSuccess ? signedIn.Value.Workspace.Id : null, cancellationToken);
        return signedIn;
    }
}
