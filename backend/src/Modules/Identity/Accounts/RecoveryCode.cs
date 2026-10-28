namespace Nihasdesk.Modules.Identity.Accounts;

/// <summary>An MFA recovery code (ADR 0015): only its SHA-256 hash is stored; usable once.</summary>
internal sealed class RecoveryCode
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    public byte[] CodeHash { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UsedAt { get; set; }
}
