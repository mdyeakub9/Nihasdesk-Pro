namespace Nihasdesk.Modules.Identity.Features;

/// <summary>ADR 0009: 12–128 characters, no composition rules. The breached-password check follows in PR 2b.</summary>
internal static class PasswordRules
{
    public const int MinLength = 12;

    public const int MaxLength = 128;

    public const string LengthMessage = "Password must be 12–128 characters.";

    public static bool IsValidLength(string? password) => password is { Length: >= MinLength and <= MaxLength };
}
