using FluentValidation;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.Products;

/// <summary>Which catalog variants to send. Only IDs: names and prices come from the catalog (SEC-22).</summary>
internal sealed record SendProductsRequest(IReadOnlyList<ProductItem>? Items);

internal sealed record ProductItem(Guid ProductId, Guid VariantId);

internal static class ProductMessageValues
{
    public const int MaxItems = 5;
}

internal sealed class SendProductsValidator : AbstractValidator<SendProductsRequest>
{
    public SendProductsValidator()
    {
        RuleFor(r => r.Items).Cascade(CascadeMode.Stop).NotEmpty().Must(i => i!.Count <= ProductMessageValues.MaxItems)
            .WithMessage($"Send 1 to {ProductMessageValues.MaxItems} products at a time.");
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
