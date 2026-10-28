namespace Nihasdesk.Modules.Identity.Accounts;

/// <summary>
/// A server-side dashboard session (ADR 0010). The cookie holds only <see cref="Id"/>; the
/// authentication ticket lives here encrypted, so a session can be revoked at any time.
/// Global like users: it is looked up before the tenant is known.
/// </summary>
internal sealed class UserSession
{
    public const int UserAgentMaxLength = 256;

    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid UserId { get; init; }

    public Guid TenantId { get; init; }

    /// <summary>"Remember me" chosen at login: long sliding lifetime instead of a browser session.</summary>
    public bool Remember { get; init; }

    /// <summary>Data-protected, serialized authentication ticket.</summary>
    public byte[] Ticket { get; set; } = [];

    public string? UserAgent { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset IdleExpiresAt { get; set; }

    public DateTimeOffset AbsoluteExpiresAt { get; init; }

    /// <summary>Last password re-check in this session (ADR 0015); a fresh sign-in counts too.</summary>
    public DateTimeOffset? ReauthenticatedAt { get; set; }
}
