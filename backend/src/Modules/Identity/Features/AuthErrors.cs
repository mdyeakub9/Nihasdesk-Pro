using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features;

/// <summary>Error catalog for the auth endpoints (docs/standards/errors.md).</summary>
internal static class AuthErrors
{
    /// <summary>Wrong email or password, or the account is locked; deliberately the same answer (SEC-26).</summary>
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Email or password is incorrect.");

    /// <summary>Only returned after the correct password, so it reveals nothing to a guesser.</summary>
    public static readonly Error EmailNotVerified =
        Error.Forbidden("auth.email_not_verified", "Confirm your email address before signing in.");

    public static readonly Error InvalidToken =
        Error.Validation("auth.invalid_token", "This link is invalid or has expired.");

    public static readonly Error NoWorkspace =
        Error.Forbidden("auth.no_workspace", "This account has no workspace.");

    public static readonly Error WeakPassword =
        Error.Validation("auth.weak_password", "Choose a different password.");
}
