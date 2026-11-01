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

/// <summary>
/// How the AI agent creates an <b>order request</b> — never a confirmed order (CLAUDE.md rule 5).
/// Validation and prices are done by code here; errors say which field to ask the customer again.
/// </summary>
public interface IOrderRequests
{
    Task<Result<OrderRequestCreated>> CreateAsync(Guid conversationId, IReadOnlyList<OrderRequestLine> lines, OrderRequestDetails details, CancellationToken cancellationToken);
}
