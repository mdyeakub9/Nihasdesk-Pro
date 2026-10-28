namespace Nihasdesk.Modules.Identity.Infrastructure.Audit;

/// <summary>
/// One security-relevant action (SEC-47). Append-only: the app role may insert and read, never
/// update or delete (enforced by the database). Kept 12 months (ADR 0012). Global, because some
/// events (failed logins) happen before a tenant is known.
/// </summary>
internal sealed class AuditEvent
{
    public const int ActionMaxLength = 64;

    public const int IpAddressMaxLength = 45;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; }

    public string Action { get; init; } = string.Empty;

    public Guid? UserId { get; init; }

    public Guid? TenantId { get; init; }

    /// <summary>Client IP for abuse detection (ADR 0012); no other request data is stored.</summary>
    public string? IpAddress { get; init; }
}

/// <summary>Stable action names; dashboards and alerts (SEC-48) rely on them.</summary>
internal static class AuditActions
{
    public const string Signup = "auth.signup";
    public const string SignupExistingEmail = "auth.signup_existing_email";
    public const string EmailVerified = "auth.email_verified";
    public const string LoginSucceeded = "auth.login_succeeded";
    public const string LoginFailed = "auth.login_failed";
    public const string LoginLockedOut = "auth.login_locked_out";
    public const string Logout = "auth.logout";
    public const string PasswordResetRequested = "auth.password_reset_requested";
    public const string PasswordReset = "auth.password_reset";
    public const string InvitationCreated = "team.invitation_created";
    public const string InvitationResent = "team.invitation_resent";
    public const string InvitationRevoked = "team.invitation_revoked";
    public const string InvitationAccepted = "team.invitation_accepted";
    public const string MemberRoleChanged = "team.member_role_changed";
    public const string MemberRemoved = "team.member_removed";
    public const string MfaEnabled = "auth.mfa_enabled";
    public const string MfaDisabled = "auth.mfa_disabled";
    public const string MfaFailed = "auth.mfa_failed";
    public const string RecoveryCodesRegenerated = "auth.recovery_codes_regenerated";
    public const string Reauthenticated = "auth.reauthenticated";
    public const string SessionRevoked = "auth.session_revoked";
    public const string PasswordChanged = "auth.password_changed";
}
