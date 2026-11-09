using FluentValidation;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.Products;

/// <summary>Which catalog products to send. Only IDs: names and prices come from the catalog (SEC-22).</summary>
internal sealed record SendProductsRequest(IReadOnlyList<ProductItem>? Items);

/// <param name="VariantId">Optional: a card for one variant; without it the card shows the product.</param>
internal sealed record ProductItem(Guid ProductId, Guid? VariantId);

internal static class ProductMessageValues
{
    /// <summary>One carousel (ADR 0032 §7).</summary>
    public const int MaxItems = ProductCarousel.MaxCards;
}

internal sealed class SendProductsValidator : AbstractValidator<SendProductsRequest>
{
    public SendProductsValidator()
    {
        RuleFor(r => r.Items).Cascade(CascadeMode.Stop).NotEmpty().Must(i => i!.Count <= ProductMessageValues.MaxItems)
            .WithMessage($"Send 1 to {ProductMessageValues.MaxItems} products at a time.")
            .Must(i => i!.Select(x => (x.ProductId, x.VariantId)).Distinct().Count() == i!.Count)
            .WithMessage("Each product only once.");
        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEqual(Guid.Empty);
            item.RuleFor(i => i.VariantId).NotEqual(Guid.Empty);
        });
    }
}

internal static class ProductMessageErrors
{
    /// <summary>Unknown, inactive or another workspace's product (SEC-06).</summary>
    public static readonly Error ProductNotFound = Error.NotFound("product.not_found", "Product not found.");

    public static readonly Error NotAvailable =
        Error.Validation("product.not_available", "This variant can't be ordered now (out of stock).");
}
