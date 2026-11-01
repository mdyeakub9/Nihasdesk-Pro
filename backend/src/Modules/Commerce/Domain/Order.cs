using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Domain;

/// <summary>PRD 4.9: an AI order request waits for a person; a team order is confirmed at once. Stored as lower-case text.</summary>
internal enum OrderStatus
{
    Pending,
    Confirmed,
    Rejected,
}

/// <summary>Who created it: the team (confirmed at once) or the AI (an order request, CLAUDE.md rule 5).</summary>
internal enum OrderSource
{
    Team,
    Ai,
}

/// <summary>Where and to whom the order goes (the default order template, PRD 4.9).</summary>
internal sealed record DeliveryDetails(
    string CustomerName,
    string Phone,
    string Street,
    string PostalCode,
    string City,
    string? Province,
    string Country,
    string? Notes);

/// <summary>Prices computed by code from the catalog and the shipping zone (SEC-41), never taken from a client or the model.</summary>
internal sealed record OrderPricing(
    IReadOnlyList<OrderLine> Lines,
    Guid ShippingZoneId,
    string ShippingZoneName,
    long ShippingCost,
    long Subtotal,
    long Total,
    string Currency);

internal sealed record OrderLine(Guid ProductId, Guid VariantId, string Title, string Variant, string? Sku, long UnitPrice, int Quantity);

/// <summary>
/// An order (ADR 0025). Lines keep the catalog title and price at the time of ordering, so later
/// catalog changes don't rewrite it. Payment is cash on delivery in Phase 1 (ADR 0004).
/// </summary>
internal sealed class Order : TenantEntity
{
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 30;
    public const int StreetMaxLength = 200;
    public const int PostalCodeMaxLength = 12;
    public const int CityMaxLength = 100;
    public const int ProvinceMaxLength = 50;
    public const int NotesMaxLength = 500;
    public const int ReasonMaxLength = 300;
    public const int ZoneNameMaxLength = 100;
    public const int FirstNumber = 1001;
    public const string CashOnDelivery = "cod";

    private readonly List<OrderItem> _items = [];

    private Order(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Per workspace, from <see cref="FirstNumber"/>: what the team and the customer call it (#1001).</summary>
    public int Number { get; private set; }

    public OrderStatus Status { get; private set; }

    public OrderSource Source { get; private set; }

    /// <summary>The chat it came from, when any; checked against the Inbox of this workspace.</summary>
    public Guid? ConversationId { get; private set; }

    public Guid? ContactId { get; private set; }

    public string CustomerName { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public string Street { get; private set; } = string.Empty;

    public string PostalCode { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string? Province { get; private set; }

    /// <summary>ISO 3166-1 alpha-2, e.g. IT.</summary>
    public string Country { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    public Guid ShippingZoneId { get; private set; }

    public string ShippingZoneName { get; private set; } = string.Empty;

    /// <summary>All amounts in minor units (cents).</summary>
    public long ShippingCost { get; private set; }

    public long Subtotal { get; private set; }

    public long Total { get; private set; }

    public string Currency { get; private set; } = "EUR";

    public string PaymentMethod { get; private set; } = CashOnDelivery;

    public Guid? CreatedByUserId { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public Guid? DecidedByUserId { get; private set; }

    public string? RejectReason { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    /// <summary>A team member's order: a person created it, so it is confirmed (PRD 4.4).</summary>
    public static Order ByTeam(Guid tenantId, int number, Guid userId, Guid? conversationId, Guid? contactId, DeliveryDetails details, OrderPricing pricing, DateTimeOffset now)
    {
        var order = New(tenantId, number, OrderSource.Team, conversationId, contactId, details, pricing);
        order.CreatedByUserId = userId;
        order.Status = OrderStatus.Confirmed;
        order.DecidedAt = now;
        order.DecidedByUserId = userId;
        return order;
    }

    /// <summary>The AI's order request: pending until a person approves it (CLAUDE.md rule 5).</summary>
    public static Order RequestFromAi(Guid tenantId, int number, Guid conversationId, Guid contactId, DeliveryDetails details, OrderPricing pricing) =>
        New(tenantId, number, OrderSource.Ai, conversationId, contactId, details, pricing);

    /// <summary>"Edit then approve": details and lines are replaced, prices computed again by the caller.</summary>
    public void Edit(DeliveryDetails details, OrderPricing pricing)
    {
        EnsurePending();
        Apply(details, pricing);
    }

    public void Approve(Guid userId, DateTimeOffset now)
    {
        EnsurePending();
        Status = OrderStatus.Confirmed;
        DecidedAt = now;
        DecidedByUserId = userId;
    }

    public void Reject(Guid userId, string reason, DateTimeOffset now)
    {
        EnsurePending();
        Status = OrderStatus.Rejected;
        RejectReason = reason.Trim();
        DecidedAt = now;
        DecidedByUserId = userId;
    }

    private static Order New(Guid tenantId, int number, OrderSource source, Guid? conversationId, Guid? contactId, DeliveryDetails details, OrderPricing pricing)
    {
        var order = new Order(tenantId) { Number = number, Source = source, ConversationId = conversationId, ContactId = contactId, Status = OrderStatus.Pending };
        order.Apply(details, pricing);
        return order;
    }

    private void Apply(DeliveryDetails details, OrderPricing pricing)
    {
        CustomerName = details.CustomerName.Trim();
        Phone = details.Phone.Trim();
        Street = details.Street.Trim();
        PostalCode = details.PostalCode.Trim().ToUpperInvariant();
        City = details.City.Trim();
        Province = string.IsNullOrWhiteSpace(details.Province) ? null : details.Province.Trim();
        Country = details.Country.Trim().ToUpperInvariant();
        Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : details.Notes.Trim();
        ShippingZoneId = pricing.ShippingZoneId;
        ShippingZoneName = pricing.ShippingZoneName.Length <= ZoneNameMaxLength ? pricing.ShippingZoneName : pricing.ShippingZoneName[..ZoneNameMaxLength];
        ShippingCost = pricing.ShippingCost;
        Subtotal = pricing.Subtotal;
        Total = pricing.Total;
        Currency = pricing.Currency;
        _items.Clear();
        _items.AddRange(pricing.Lines.Select((line, index) => OrderItem.Create(TenantId, Id, line, index)));
    }

    private void EnsurePending()
    {
        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException("Only pending order requests can be changed.");
        }
    }
}

/// <summary>One line of an order: a catalog variant, its price then, and the quantity.</summary>
internal sealed class OrderItem : TenantEntity
{
    public const int TitleMaxLength = 255;
    public const int VariantMaxLength = 160;

    private OrderItem(Guid tenantId, Guid orderId)
        : base(tenantId)
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid VariantId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Option values, e.g. "Nero / M"; empty when the product has none.</summary>
    public string Variant { get; private set; } = string.Empty;

    public string? Sku { get; private set; }

    public long UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public long LineTotal { get; private set; }

    public int Position { get; private set; }

    public static OrderItem Create(Guid tenantId, Guid orderId, OrderLine line, int position) => new(tenantId, orderId)
    {
        ProductId = line.ProductId,
        VariantId = line.VariantId,
        Title = line.Title.Length <= TitleMaxLength ? line.Title : line.Title[..TitleMaxLength],
        Variant = line.Variant.Length <= VariantMaxLength ? line.Variant : line.Variant[..VariantMaxLength],
        Sku = line.Sku,
        UnitPrice = line.UnitPrice,
        Quantity = line.Quantity,
        LineTotal = line.UnitPrice * line.Quantity,
        Position = position,
    };
}
