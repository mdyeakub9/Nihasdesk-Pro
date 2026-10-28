using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Email.Outbox;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Nihasdesk.Modules.Identity.Infrastructure.Email;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features.ResetPassword;

internal sealed record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.Token).NotEmpty().MaximumLength(2048);
        RuleFor(r => r.NewPassword).Must(PasswordRules.IsValidLength).WithMessage(PasswordRules.LengthMessage);
    }
}

/// <summary>
/// Sets the new password (runs the password policy incl. HIBP). The token works once because the
/// security stamp changes. Afterwards every session of the user is revoked (SEC-26), lockout is
/// cleared and a notice is emailed. No automatic sign-in.
/// </summary>
internal sealed class ResetPasswordHandler(
    UserManager<User> users,
    IdentityModuleDbContext db,
    IEmailOutbox email,
    DashboardLinks links,
    AuditLog audit)
{
    public async Task<Result> HandleAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return Result.Failure(AuthErrors.InvalidToken);
        }

        var reset = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!reset.Succeeded)
        {
            return Result.Failure(reset.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? AuthErrors.InvalidToken
                : AuthErrors.ForPassword(reset.Errors));
        }

        await db.Sessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, lockoutEnd: null);

        await email.EnqueueAsync(AuthEmails.PasswordChanged(user.Email!, user.Language, user.DisplayName, links.Login), cancellationToken);
        await audit.WriteAsync(AuditActions.PasswordReset, user.Id, tenantId: null, cancellationToken);
        return Result.Success();
    }
}
