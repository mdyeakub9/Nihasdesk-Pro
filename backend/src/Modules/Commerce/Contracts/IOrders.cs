using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Contracts;

/// <summary>
/// What Commerce needs from the Inbox about the chat an order comes from (ADR 0025). Defined here
/// and implemented by the Inbox, so the modules don't reference each other in a circle.
/// </summary>
public interface IOrderConversations
{
    /// <summary>The conversation's contact in the current workspace; null when it isn't one of its conversations.</summary>
    Task<Guid?> ContactIdAsync(Guid conversationId, CancellationToken cancellationToken);
}

/// <summary>A line the AI collected: catalog IDs and a quantity only; prices come from the catalog.</summary>
public sealed record OrderRequestLine(Guid ProductId, Guid VariantId, int Quantity);

/// <summary>The default order template's fields (PRD 4.9), as collected in the chat.</summary>
public sealed record OrderRequestDetails(
    string CustomerName,
    string Phone,
    string Street,
    string PostalCode,
    string City,
    string? Province,
    string Country,
    Guid ShippingZoneId,
    string? Notes);

/// <summary>A created order request, for the AI's summary to the customer. Amounts in cents.</summary>
public sealed record OrderRequestCreated(Guid OrderId, int Number, long Subtotal, long ShippingCost, long Total, string Currency);

/// <summary>One priced line of a quote. Amounts in cents, from the catalog.</summary>
public sealed record OrderQuoteLine(Guid ProductId, Guid VariantId, string Title, string Variant, long UnitPrice, int Quantity);

/// <summary>What an order would cost (catalog + shipping zone, by code); nothing is saved.</summary>
public sealed record OrderQuote(IReadOnlyList<OrderQuoteLine> Lines, string ShippingZoneName, long Subtotal, long ShippingCost, long Total, string Currency);

/// <summary>
/// How the AI agent creates an <b>order request</b> — never a confirmed order (CLAUDE.md rule 5).
/// Validation and prices are done by code here; errors say which field to ask the customer again.
/// </summary>
public interface IOrderRequests
{
    /// <summary>The summary the AI shows before the customer confirms (ADR 0025).</summary>
    Task<Result<OrderQuote>> QuoteAsync(IReadOnlyList<OrderRequestLine> lines, Guid shippingZoneId, CancellationToken cancellationToken);

    /// <summary>At most one pending request per conversation (SEC-42): a second one answers <c>order.pending_exists</c>.</summary>
    Task<Result<OrderRequestCreated>> CreateAsync(Guid conversationId, IReadOnlyList<OrderRequestLine> lines, OrderRequestDetails details, CancellationToken cancellationToken);
}

/// <summary>One line of a confirmed order, as the customer sees it. Amounts in cents.</summary>
public sealed record ConfirmedOrderLine(string Title, string Variant, int Quantity, long LineTotal);

/// <summary>What the customer is told when their order is confirmed (ADR 0025). Amounts in cents.</summary>
public sealed record ConfirmedOrderSummary(
    int Number,
    IReadOnlyList<ConfirmedOrderLine> Lines,
    string ShippingZoneName,
    long ShippingCost,
    long Total,
    string Currency,
    string Street,
    string PostalCode,
    string City);

/// <summary>
/// What happened to an order of a chat, for that chat (ADR 0025). Defined here and
/// implemented by the Inbox: it tells the team and writes to the customer within the channel's rules.
/// </summary>
public interface IOrderNotifications
{
    /// <summary>A new request waits for a person: the team's dashboards are told (IDs only).</summary>
    Task RequestCreatedAsync(Guid conversationId, Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// An order of this chat is confirmed — a team member created it, or approved the AI's request:
    /// the customer gets the summary (lines, shipping, total to pay on delivery, address).
    /// </summary>
    Task ConfirmedAsync(Guid conversationId, ConfirmedOrderSummary order, CancellationToken cancellationToken);

    /// <summary>A person rejected it: the customer gets a polite message; the reason stays with the team.</summary>
    Task RejectedAsync(Guid conversationId, int number, CancellationToken cancellationToken);
}
