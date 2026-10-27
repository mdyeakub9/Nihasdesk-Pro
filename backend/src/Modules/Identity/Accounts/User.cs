using Microsoft.AspNetCore.Identity;

namespace Nihasdesk.Modules.Identity.Accounts;

/// <summary>
/// A login account (ASP.NET Core Identity). Global, not tenant-owned: one person can belong to
/// several workspaces through memberships. Kept outside <c>Domain/</c> because it derives from a
/// framework type.
/// </summary>
internal sealed class User : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 200;

    public const int LanguageMaxLength = 10;

    public User()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Dashboard and email language (PRD 4.13): "it" or "en".</summary>
    public string Language { get; set; } = "en";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
