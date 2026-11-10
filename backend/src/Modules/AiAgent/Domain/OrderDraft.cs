using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Domain;

/// <summary>
/// What a customer has chosen so far in one conversation (ADR 0032 §4, owner decision B): the product
/// they tapped, the option values picked one by one, the items of the order they are building, and the
/// last summary sent with Conferma / Modifica. Written by code (taps, <c>quote_order</c>), never by the
/// model. Holds delivery details only between the summary and the order request; deleted when the
/// request is created, and by the retention job after <see cref="Lifetime"/> (SEC-44). The contact is
/// kept so it can be erased with them (SEC-45).
/// </summary>
internal sealed class OrderDraft : TenantEntity
{
    public const int MaxLines = 20;
    public const int MaxChosenValues = 3;
    public const int ValueMaxLength = 255;
    public const int QueryMaxLength = 200;
    public const int SummaryHashLength = 64;
    public const int MaxShownProducts = 50;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private OrderDraft(Guid tenantId)
        : base(tenantId)
    {
    }

    public Guid ConversationId { get; private set; }

    public Guid ContactId { get; private set; }

    /// <summary>The product whose options are being chosen; null when none.</summary>
    public Guid? ProductId { get; private set; }

    /// <summary>A variant chosen up front (a card for one variant, a photo match), still to be confirmed.</summary>
    public Guid? PreselectedVariantId { get; private set; }

    /// <summary>Option values picked so far for <see cref="ProductId"/>, in option order (colour, then size).</summary>
    public List<string> ChosenValues { get; private set; } = [];

    /// <summary>Items of the order being built (product, variant, quantity); ids only, prices come from the catalog.</summary>
    public List<DraftLine> Lines { get; private set; } = [];

    /// <summary>The words of the last product search shown as a carousel, for "Altri prodotti".</summary>
    public string? LastQuery { get; private set; }

    /// <summary>Products already shown for <see cref="LastQuery"/>, so the next page has new ones.</summary>
    public List<Guid> ShownProductIds { get; private set; } = [];

    /// <summary>Delivery zone and details of the last summary; the inputs of <see cref="SummaryHash"/>.</summary>
    public DraftSummary? Summary { get; private set; }

    /// <summary>SHA-256 of items, prices, shipping and address when the summary was sent (PRD 4.9 step 5).</summary>
    public string? SummaryHash { get; private set; }

    public static OrderDraft Start(Guid tenantId, Guid conversationId, Guid contactId) =>
        new(tenantId) { ConversationId = conversationId, ContactId = contactId };

    /// <summary>Starts choosing options of a product; a variant chosen up front is only confirmed.</summary>
    public void Choose(Guid productId, Guid? preselectedVariantId)
    {
        ProductId = productId;
        PreselectedVariantId = preselectedVariantId;
        ChosenValues = [];
    }

    public void PickValue(string value)
    {
        if (ProductId is null || ChosenValues.Count >= MaxChosenValues)
        {
            throw new InvalidOperationException("No product whose options are being chosen.");
        }

        ChosenValues = [.. ChosenValues, value.Length <= ValueMaxLength ? value : value[..ValueMaxLength]];
    }

    /// <summary>The variant is complete: it becomes an item (once), and choosing starts over.</summary>
    public void AddLine(Guid productId, Guid variantId)
    {
        if (!Lines.Any(l => l.VariantId == variantId))
        {
            if (Lines.Count >= MaxLines)
            {
                throw new InvalidOperationException("Too many items.");
            }

            Lines = [.. Lines, new DraftLine(productId, variantId, 1)];
        }

        ProductId = null;
        PreselectedVariantId = null;
        ChosenValues = [];
        ClearSummary();
    }

    public void Shown(string query, IEnumerable<Guid> productIds, bool nextPage)
    {
        LastQuery = query.Length <= QueryMaxLength ? query : query[..QueryMaxLength];
        ShownProductIds = [.. (nextPage ? ShownProductIds : []).Concat(productIds).Distinct().TakeLast(MaxShownProducts)];
    }

    /// <summary>The summary the customer is asked to confirm; replaces the items with the quoted ones.</summary>
    public void Summarized(IReadOnlyList<DraftLine> lines, DraftSummary summary, string hash)
    {
        Lines = [.. lines.Take(MaxLines)];
        Summary = summary;
        SummaryHash = hash;
    }

    /// <summary>After "Modifica" or a changed cart: the old Conferma no longer works.</summary>
    public void ClearSummary()
    {
        Summary = null;
        SummaryHash = null;
    }

    public bool IsExpired(DateTimeOffset now) => now - UpdatedAt > Lifetime;
}

/// <summary>One item: ids and quantity only.</summary>
internal sealed record DraftLine(Guid ProductId, Guid VariantId, int Quantity);

/// <summary>The delivery part of a summary, kept only until the order request is created.</summary>
internal sealed record DraftSummary(
    Guid ZoneId,
    string CustomerName,
    string Phone,
    string Street,
    string PostalCode,
    string City,
    string? Province,
    string Country,
    string? Notes);
