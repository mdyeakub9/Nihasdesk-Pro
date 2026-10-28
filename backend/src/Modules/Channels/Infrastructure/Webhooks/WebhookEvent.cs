using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Channels.Infrastructure.Webhooks;

/// <summary>
/// A verified webhook delivery, stored before processing (ADR 0017, docs/standards/integrations.md).
/// The payload holds personal data, so it is encrypted with Data Protection and purged 7 days after processing.
/// </summary>
internal sealed class WebhookEvent : Entity
{
    public const int ProviderMaxLength = 20;
    public const int LastErrorMaxLength = 100;

    public string Provider { get; init; } = string.Empty;

    public byte[] ProtectedPayload { get; set; } = [];

    public DateTimeOffset ReceivedAt { get; init; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public int Attempts { get; set; }

    /// <summary>Exception type only, never payload content.</summary>
    public string? LastError { get; set; }

    /// <summary>Set when handled, or when given up after the last attempt.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }
}
