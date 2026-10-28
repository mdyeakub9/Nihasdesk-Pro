using FluentValidation;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Domain;

namespace Nihasdesk.Modules.Identity.Features.Team;

/// <summary>Roles as sent by clients: only "admin" or "member" can be given (SEC-22: never "owner").</summary>
internal static class AssignableRoles
{
    public static bool IsValid(string? role) => role is "admin" or "member";

    public static Role Parse(string role) => role == "admin" ? Role.Admin : Role.Member;
}

internal sealed record InviteRequest(string Email, string Role);

internal sealed class InviteValidator : AbstractValidator<InviteRequest>
{
    public InviteValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(Invitation.EmailMaxLength);
        RuleFor(r => r.Role).Must(AssignableRoles.IsValid).WithMessage("Role must be 'admin' or 'member'.");
    }
}

internal sealed record ChangeRoleRequest(string Role);

internal sealed class ChangeRoleValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleValidator() =>
        RuleFor(r => r.Role).Must(AssignableRoles.IsValid).WithMessage("Role must be 'admin' or 'member'.");
}

internal sealed record AcceptInvitationRequest(string Token, string Name, string Password);

internal sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationValidator()
    {
        RuleFor(r => r.Token).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(User.DisplayNameMaxLength);
        RuleFor(r => r.Password).Must(PasswordRules.IsValidLength).WithMessage(PasswordRules.LengthMessage);
    }
}

internal sealed record MemberResponse(Guid UserId, string Name, string Email, string Role, DateTimeOffset JoinedAt);

internal sealed record InvitationResponse(Guid Id, string Email, string Role, DateTimeOffset InvitedAt, DateTimeOffset ExpiresAt);

internal sealed record InvitationPreviewResponse(string WorkspaceName, string InviterName, string Email, string Role, DateTimeOffset ExpiresAt);
