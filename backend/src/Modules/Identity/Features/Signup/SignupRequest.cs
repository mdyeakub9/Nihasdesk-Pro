using FluentValidation;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Domain;

namespace Nihasdesk.Modules.Identity.Features.Signup;

/// <summary>No TenantId, role or IDs: those are server-controlled (SEC-22).</summary>
internal sealed record SignupRequest(string Name, string Email, string Password, string BusinessName, string? Language);

internal sealed class SignupValidator : AbstractValidator<SignupRequest>
{
    public SignupValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(User.DisplayNameMaxLength);
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(r => r.Password).Must(PasswordRules.IsValidLength).WithMessage(PasswordRules.LengthMessage);
        RuleFor(r => r.BusinessName).NotEmpty().MaximumLength(Workspace.NameMaxLength);
        RuleFor(r => r.Language).Must(l => l is null or "it" or "en").WithMessage("Language must be 'it' or 'en'.");
    }
}
