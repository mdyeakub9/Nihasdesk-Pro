using System.Text.RegularExpressions;
using FluentValidation;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Features.Orders;

/// <summary>A line to order: catalog IDs and a quantity. Prices are never accepted from clients (SEC-22, SEC-41).</summary>
internal sealed record OrderItemRequest(Guid ProductId, Guid VariantId, int Quantity);

/// <summary>Create (team order) or edit (pending request) an order; the default order template (PRD 4.9).</summary>
internal sealed record OrderRequest(
    Guid? ConversationId,
    string? CustomerName,
    string? Phone,
    string? Street,
    string? PostalCode,
    string? City,
    string? Province,
    string? Country,
    Guid ShippingZoneId,
    string? Notes,
    IReadOnlyList<OrderItemRequest>? Items);

internal sealed record RejectRequest(string? Reason);

internal sealed record OrderItemResponse(Guid ProductId, Guid VariantId, string Title, string Variant, string? Sku, long UnitPrice, int Quantity, long LineTotal);

internal sealed record OrderResponse(
    Guid Id,
    int Number,
    string Status,
    string Source,
    Guid? ConversationId,
    string CustomerName,
    string Phone,
    string Street,
    string PostalCode,
    string City,
    string? Province,
    string Country,
    string? Notes,
    Guid ShippingZoneId,
    string ShippingZoneName,
    long ShippingCost,
    long Subtotal,
    long Total,
    string Currency,
    string PaymentMethod,
    IReadOnlyList<OrderItemResponse> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? RejectReason);

internal sealed record OrderSummaryResponse(
    Guid Id,
    int Number,
    string Status,
    string Source,
    string CustomerName,
    long Total,
    string Currency,
    int ItemCount,
    DateTimeOffset CreatedAt);

internal sealed record OrderPageResponse(IReadOnlyList<OrderSummaryResponse> Items, string? NextCursor);

internal static class OrderValues
{
    public const int MaxLines = 20;
    public const int MaxQuantity = 99;
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 100;
}

/// <summary>Validation by code, not by the model (PRD 4.9): the AI asks again for what fails here.</summary>
internal sealed partial class OrderValidator : AbstractValidator<OrderRequest>
{
    public OrderValidator()
    {
        RuleFor(r => r.CustomerName).NotEmpty().Must(NotBlank).MaximumLength(Order.NameMaxLength);
        RuleFor(r => r.Phone).NotEmpty().Must(p => p is not null && Phone().IsMatch(p.Trim()))
            .WithMessage("Phone must be digits, spaces and an optional leading +, e.g. +39 333 123 4567.");
        RuleFor(r => r.Street).NotEmpty().Must(NotBlank).MaximumLength(Order.StreetMaxLength);
        RuleFor(r => r.City).NotEmpty().Must(NotBlank).MaximumLength(Order.CityMaxLength);
        RuleFor(r => r.Province).MaximumLength(Order.ProvinceMaxLength);
        RuleFor(r => r.Country).NotEmpty().Must(c => c is not null && Country().IsMatch(c.Trim().ToUpperInvariant()))
            .WithMessage("Country must be a two-letter code, e.g. IT.");
        RuleFor(r => r.PostalCode).NotEmpty().Must((r, code) => PostalCodeOk(r.Country, code))
            .WithMessage("Postal code is not valid (Italy: 5 digits, e.g. 07100).");
        RuleFor(r => r.ShippingZoneId).NotEqual(Guid.Empty).WithMessage("Choose a delivery zone.");
        RuleFor(r => r.Notes).MaximumLength(Order.NotesMaxLength);
        RuleFor(r => r.Items).Cascade(CascadeMode.Stop).NotEmpty()
            .Must(i => i!.Count <= OrderValues.MaxLines).WithMessage($"At most {OrderValues.MaxLines} lines.");
        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEqual(Guid.Empty);
            item.RuleFor(i => i.VariantId).NotEqual(Guid.Empty);
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, OrderValues.MaxQuantity);
        });
    }

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool PostalCodeOk(string? country, string? code)
    {
        var value = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return country?.Trim().ToUpperInvariant() == "IT" ? ItalianCap().IsMatch(value) : OtherPostalCode().IsMatch(value);
    }

    [GeneratedRegex(@"^\+?[0-9][0-9 ]{5,19}$")]
    private static partial Regex Phone();

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex Country();

    [GeneratedRegex(@"^\d{5}$")]
    private static partial Regex ItalianCap();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9 -]{1,10}$")]
    private static partial Regex OtherPostalCode();
}

internal sealed class RejectValidator : AbstractValidator<RejectRequest>
{
    public RejectValidator()
    {
        RuleFor(r => r.Reason).NotEmpty().Must(r => !string.IsNullOrWhiteSpace(r)).MaximumLength(Order.ReasonMaxLength);
    }
}

internal static class OrderErrors
{
    /// <summary>Also for other workspaces' orders (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("order.not_found", "Order not found.");

    public static readonly Error ConversationNotFound = Error.NotFound("conversation.not_found", "Conversation not found.");

    /// <summary>Unknown, inactive or another workspace's product, or a variant of another product.</summary>
    public static readonly Error ProductNotFound = Error.NotFound("product.not_found", "Product not found.");

    public static readonly Error NotAvailable = Error.Conflict("product.not_available", "Not enough stock for one of the products.");

    public static readonly Error ZoneNotFound = Error.Validation("order.zone_not_found", "This delivery zone doesn't exist. Choose one from Settings → Shop knowledge.");

    public static readonly Error MixedCurrency = Error.Validation("order.mixed_currency", "All products and the delivery zone must use the same currency.");

    public static readonly Error NotPending = Error.Conflict("order.not_pending", "This order was already approved or rejected.");
}
