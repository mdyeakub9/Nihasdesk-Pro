using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Email;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure.Email;

namespace Nihasdesk.Modules.Identity.Features.ResendVerification;

internal sealed record ResendVerificationRequest(string Email);

internal sealed class ResendVerificationValidator : AbstractValidator<ResendVerificationRequest>
{
    public ResendVerificationValidator() => RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

/// <summary>Sends a new verification link if the account exists and is unverified; the answer is always the same.</summary>
internal sealed class ResendVerificationHandler(UserManager<User> users, IEmailSender email, IOptions<DashboardOptions> dashboard)
{
    public async Task HandleAsync(ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = dashboard.Value.DashboardUrl.TrimEnd('/') + $"/verify-email?user={user.Id}&token={Uri.EscapeDataString(token)}";
        await email.SendAsync(AuthEmails.VerifyEmail(user.Email!, user.Language, user.DisplayName, link), cancellationToken);
    }
}
