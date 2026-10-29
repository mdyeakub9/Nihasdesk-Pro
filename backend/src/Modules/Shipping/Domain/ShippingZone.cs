using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Shipping.Domain;

/// <summary>
/// Where the shop delivers and what it costs (PRD 4.7, ADR 0020), e.g. "Italia" €5.90, free over
/// €50, 2–4 days. Money in minor units (cents). The AI quotes these through a tool; order
/// summaries will compute shipping from them (SEC-41), never from the model.
/// </summary>
internal sealed class ShippingZone : TenantEntity
{
    public const int NameMaxLength = 100;
    public const int AreasMaxLength = 500;
    public const int CurrencyLength = 3;
    public const long MaxPrice = 100_000_00;
    public const int DaysLimit = 365;

    private ShippingZone(Guid tenantId, string name)
        : base(tenantId)
    {
        Name = name;
    }

    public string Name { get; private set; }

    /// <summary>Which places the zone covers, in the owner's words: "Sardegna, Sicilia, isole minori".</summary>
    public string? Areas { get; private set; }

    public long Price { get; private set; }

    public string Currency { get; private set; } = "EUR";

    /// <summary>Order value from which shipping is free; null = never free.</summary>
    public long? FreeOver { get; private set; }

    public int? MinDays { get; private set; }

    public int? MaxDays { get; private set; }

    public int Position { get; private set; }

    public static ShippingZone Create(
        Guid tenantId, int position, string name, string? areas, long price, string currency, long? freeOver, int? minDays, int? maxDays)
    {
        var zone = new ShippingZone(tenantId, name);
        zone.Update(position, name, areas, price, currency, freeOver, minDays, maxDays);
        return zone;
    }

    public void Update(int position, string name, string? areas, long price, string currency, long? freeOver, int? minDays, int? maxDays)
    {
        Name = name.Trim();
        Areas = string.IsNullOrWhiteSpace(areas) ? null : areas.Trim();
        Price = price;
        Currency = currency;
        FreeOver = freeOver;
        MinDays = minDays;
        MaxDays = maxDays;
        Position = position;
    }
}
