using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Nihasdesk.Infrastructure.Email;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Nihasdesk.Modules.Identity.Infrastructure.Email;

namespace Nihasdesk.Modules.Identity.Features.ForgotPassword;

internal sealed record ForgotPasswordRequest(string Email);

internal sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordValidator() => RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

/// <summary>
/// Sends a reset link (valid 1 hour) to verified accounts. The answer is the same for unknown,
/// unverified and verified emails (SEC-26).
/// </summary>
internal sealed class ForgotPasswordHandler(UserManager<User> users, IEmailSender email, DashboardLinks links, AuditLog audit)
{
    public async Task HandleAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.EmailConfirmed)
        {
            return;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        await email.SendAsync(
            AuthEmails.ResetPassword(user.Email!, user.Language, user.DisplayName, links.ResetPassword(user.Id, token)),
            cancellationToken);
        await audit.WriteAsync(AuditActions.PasswordResetRequested, user.Id, tenantId: null, cancellationToken);
    }
}
