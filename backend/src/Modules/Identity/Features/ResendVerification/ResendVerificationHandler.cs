using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Nihasdesk.Infrastructure.Email.Outbox;
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
internal sealed class ResendVerificationHandler(UserManager<User> users, IEmailOutbox email, DashboardLinks links)
{
    public async Task HandleAsync(ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await email.EnqueueAsync(
            AuthEmails.VerifyEmail(user.Email!, user.Language, user.DisplayName, links.VerifyEmail(user.Id, token)),
            cancellationToken);
    }
}
