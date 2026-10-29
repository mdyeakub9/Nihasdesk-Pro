namespace Nihasdesk.Modules.Shipping.Contracts;

/// <summary>One delivery zone with its cost in minor units (cents).</summary>
public sealed record ShippingRate(
    Guid Id,
    string Name,
    string? Areas,
    long Price,
    string Currency,
    long? FreeOver,
    int? MinDays,
    int? MaxDays);

/// <summary>
/// The current workspace's delivery zones (ADR 0020). The one source of shipping costs for the AI
/// and, later, order summaries (CLAUDE.md rule 5, SEC-41).
/// </summary>
public interface IShippingRates
{
    /// <summary>All zones in the owner's order.</summary>
    Task<IReadOnlyList<ShippingRate>> ListAsync(CancellationToken cancellationToken);
}
