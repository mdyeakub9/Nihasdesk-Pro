using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Contacts.Domain;

/// <summary>
/// A product the contact tapped on (PRD 4.5 "products of interest"). One row per contact and product;
/// only the product ID is kept, its name is read from the catalog when shown. Deleted with the contact.
/// </summary>
internal sealed class ProductInterest : TenantEntity
{
    public const int ActionMaxLength = 20;

    private ProductInterest(Guid tenantId, Guid contactId, Guid productId, string lastAction, DateTimeOffset firstAt)
        : base(tenantId)
    {
        ContactId = contactId;
        ProductId = productId;
        LastAction = lastAction;
        Wanted = lastAction == InterestAction.Buy;
        Taps = 1;
        FirstAt = firstAt;
        LastAt = firstAt;
    }

    public Guid ContactId { get; private set; }

    public Guid ProductId { get; private set; }

    /// <summary>One of <see cref="InterestAction"/>.</summary>
    public string LastAction { get; private set; }

    /// <summary>Tapped "Lo voglio" at least once; a later "Simili" doesn't undo it.</summary>
    public bool Wanted { get; private set; }

    public int Taps { get; private set; }

    public DateTimeOffset FirstAt { get; private set; }

    public DateTimeOffset LastAt { get; private set; }

    public static ProductInterest Create(Guid tenantId, Guid contactId, Guid productId, string action, DateTimeOffset at)
    {
        if (contactId == Guid.Empty || productId == Guid.Empty)
        {
            throw new ArgumentException("Contact and product are required.");
        }

        return new ProductInterest(tenantId, contactId, productId, Check(action), at);
    }

    public void Tapped(string action, DateTimeOffset at)
    {
        LastAction = Check(action);
        Wanted |= action == InterestAction.Buy;
        Taps = Taps == int.MaxValue ? Taps : Taps + 1;
        if (at > LastAt)
        {
            LastAt = at;
        }
    }

    private static string Check(string action) =>
        InterestAction.All.Contains(action) ? action : throw new ArgumentException($"Unknown interest action '{action}'.", nameof(action));
}
