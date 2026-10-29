using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Domain;

/// <summary>
/// The shop's rules the AI may tell customers (PRD 4.7, ADR 0020): returns, payment, contact and
/// general shipping notes. One row per workspace. Shipping costs live in the Shipping module.
/// </summary>
internal sealed class ShopPolicies : TenantEntity
{
    public const int ShippingNotesMaxLength = 1000;
    public const int ReturnPolicyMaxLength = 2000;
    public const int MaxReturnDays = 365;
    public const int PaymentNotesMaxLength = 1000;
    public const int PhoneMaxLength = 40;
    public const int EmailMaxLength = 254;
    public const int AddressMaxLength = 300;
    public const int OpeningHoursMaxLength = 500;

    /// <summary>Phase 1 has one payment method (ADR 0004); more come with Stripe Connect.</summary>
    public static readonly IReadOnlyList<string> PaymentMethods = ["cash_on_delivery"];

    private ShopPolicies(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>E.g. "Corriere BRT, tracking by SMS. Orders before 13:00 leave the same day."</summary>
    public string? ShippingNotes { get; private set; }

    /// <summary>Days after delivery in which customers may return; null = not set.</summary>
    public int? ReturnDays { get; private set; }

    public string? ReturnPolicy { get; private set; }

    public string? PaymentNotes { get; private set; }

    public string? ContactPhone { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? ContactAddress { get; private set; }

    public string? OpeningHours { get; private set; }

    public static ShopPolicies Empty(Guid tenantId) => new(tenantId);

    public void Update(
        string? shippingNotes, int? returnDays, string? returnPolicy, string? paymentNotes,
        string? contactPhone, string? contactEmail, string? contactAddress, string? openingHours)
    {
        ShippingNotes = Clean(shippingNotes, ShippingNotesMaxLength);
        ReturnDays = returnDays;
        ReturnPolicy = Clean(returnPolicy, ReturnPolicyMaxLength);
        PaymentNotes = Clean(paymentNotes, PaymentNotesMaxLength);
        ContactPhone = Clean(contactPhone, PhoneMaxLength);
        ContactEmail = Clean(contactEmail, EmailMaxLength);
        ContactAddress = Clean(contactAddress, AddressMaxLength);
        OpeningHours = Clean(openingHours, OpeningHoursMaxLength);
    }

    private static string? Clean(string? text, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
