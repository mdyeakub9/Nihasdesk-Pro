namespace Nihasdesk.Modules.Contacts.Contracts;

/// <summary>Which tap showed the interest (PRD 4.5). Stored as this text.</summary>
public static class InterestAction
{
    /// <summary>"Lo voglio": the customer wants to buy it.</summary>
    public const string Buy = "buy";

    /// <summary>"Taglie e colori".</summary>
    public const string Options = "options";

    /// <summary>"Simili".</summary>
    public const string Similar = "similar";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Buy, Options, Similar };
}

/// <summary>
/// One product a contact showed interest in. <paramref name="Wanted"/> is true once they tapped
/// "Lo voglio" on it; <paramref name="LastAction"/> is one of <see cref="InterestAction"/>.
/// </summary>
public sealed record ProductInterestEntry(
    Guid ProductId,
    string LastAction,
    bool Wanted,
    int Taps,
    DateTimeOffset FirstAt,
    DateTimeOffset LastAt);

/// <summary>
/// The contact's products of interest (ADR 0032 §6, owner decision A: called in process instead of an
/// event). Current workspace only; the caller has already checked the product is in this workspace.
/// </summary>
public interface IContactInterest
{
    /// <summary>Adds the product or counts one more tap. Does nothing for a contact of another workspace.</summary>
    Task RecordAsync(Guid contactId, Guid productId, string action, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Newest first, at most <see cref="MaxListed"/>.</summary>
    Task<IReadOnlyList<ProductInterestEntry>> ListAsync(Guid contactId, CancellationToken cancellationToken);

    public const int MaxListed = 20;
}
