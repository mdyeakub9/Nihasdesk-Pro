namespace Nihasdesk.Modules.Inbox.Contracts;

/// <summary>A team reply waiting to be sent to the customer's channel (ADR 0017).</summary>
public sealed record OutboundMessage(
    Guid MessageId,
    Guid ConversationId,
    string Channel,
    Guid ConnectionId,
    Guid ContactId,
    string Text,
    int Attempts);

/// <summary>A reply that is due, in some workspace. Found across workspaces by a narrow database lookup.</summary>
public sealed record DueDelivery(Guid TenantId, Guid MessageId);

/// <summary>What the channel answered.</summary>
public sealed record DeliveryOutcome(string? ExternalId, string? Error, bool Retry)
{
    public static DeliveryOutcome Sent(string externalId) => new(externalId, null, false);

    /// <summary>Temporary: network, rate limit, Meta outage. Tried again later.</summary>
    public static DeliveryOutcome Retryable(string error) => new(null, error, true);

    /// <summary>Will never work: blocked by the customer, outside the window, invalid recipient.</summary>
    public static DeliveryOutcome Permanent(string error) => new(null, error, false);
}

/// <summary>How channel modules send team replies; the Inbox keeps the delivery state.</summary>
public interface IOutboundMessages
{
    /// <summary>Due replies of all workspaces, oldest first. Needs no tenant; returns IDs only.</summary>
    Task<IReadOnlyList<DueDelivery>> DueAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// In the message's workspace scope: locks the reply (so two senders never send it twice),
    /// re-checks the 24-hour window, calls <paramref name="send"/> and records the outcome.
    /// Returns false when the reply was not due or another sender has it.
    /// </summary>
    Task<bool> DeliverAsync(
        Guid messageId,
        Func<OutboundMessage, CancellationToken, Task<DeliveryOutcome>> send,
        CancellationToken cancellationToken);
}
