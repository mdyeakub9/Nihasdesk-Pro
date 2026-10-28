using FluentValidation;

namespace Nihasdesk.Modules.Identity.Features.Account;

/// <summary>Either the 6-digit authenticator code or one recovery code.</summary>
internal sealed record MfaLoginRequest(string? Code, string? RecoveryCode);

internal sealed class MfaLoginValidator : AbstractValidator<MfaLoginRequest>
{
    public MfaLoginValidator()
    {
        RuleFor(r => r).Must(r => !string.IsNullOrWhiteSpace(r.Code) ^ !string.IsNullOrWhiteSpace(r.RecoveryCode))
            .WithMessage("Send either a code or a recovery code.");
        RuleFor(r => r.Code).MaximumLength(10);
        RuleFor(r => r.RecoveryCode).MaximumLength(30);
    }
}

internal sealed record MfaCodeRequest(string Code);

internal sealed class MfaCodeValidator : AbstractValidator<MfaCodeRequest>
{
    public MfaCodeValidator() => RuleFor(r => r.Code).NotEmpty().MaximumLength(10);
}

internal sealed record ReauthRequest(string Password);

internal sealed class ReauthValidator : AbstractValidator<ReauthRequest>
{
    public ReauthValidator() => RuleFor(r => r.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
}

internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty().MaximumLength(PasswordRules.MaxLength);
        RuleFor(r => r.NewPassword).Must(PasswordRules.IsValidLength).WithMessage(PasswordRules.LengthMessage);
    }
}

internal sealed record MfaStatusResponse(bool Enabled, int RecoveryCodesLeft);

/// <summary>Shown once while setting MFA up; the dashboard draws the QR code locally.</summary>
internal sealed record MfaSetupResponse(string Secret, string OtpauthUri);

internal sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

internal sealed record SessionResponse(
    Guid Id,
    string? UserAgent,
    bool Remember,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    bool Current);
