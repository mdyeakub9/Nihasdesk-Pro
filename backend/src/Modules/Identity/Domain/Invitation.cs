using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Domain;

/// <summary>
/// An invitation to join a workspace (ADR 0014). Only the SHA-256 hash of the token is stored;
/// the token itself exists only in the email. Valid for 48 hours, single use, revocable.
/// </summary>
internal sealed class Invitation : TenantEntity
{
    public const int EmailMaxLength = 256;

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);

    private Invitation(Guid tenantId, string email, Role role, byte[] tokenHash, Guid invitedBy, DateTimeOffset expiresAt)
        : base(tenantId)
    {
        Email = email;
        Role = role;
        TokenHash = tokenHash;
        InvitedBy = invitedBy;
        ExpiresAt = expiresAt;
    }

    /// <summary>Lower-case, trimmed.</summary>
    public string Email { get; private set; }

    public Role Role { get; private set; }

    public byte[] TokenHash { get; private set; }

    public Guid InvitedBy { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static Invitation Create(Guid tenantId, string email, Role role, byte[] tokenHash, Guid invitedBy, DateTimeOffset now)
    {
        if (role == Role.Owner)
        {
            throw new ArgumentException("A workspace has one Owner; invitations are for Admins and Members.", nameof(role));
        }

        if (tokenHash.Length != 32)
        {
            throw new ArgumentException("Expected a SHA-256 hash.", nameof(tokenHash));
        }

        return new Invitation(tenantId, NormalizeEmail(email), role, tokenHash, invitedBy, now + Lifetime);
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public bool IsUsable(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    public void Accept(DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            throw new InvalidOperationException("Invitation is not usable.");
        }

        AcceptedAt = now;
    }
}
