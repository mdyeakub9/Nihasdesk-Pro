using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Features.Conversations;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.Products;

/// <summary>
/// "Send products" from the + menu (PRD 4.4, ADR 0032 §7): up to 10 products as one carousel with
/// the standard buttons. The message holds product ids only; the channel sender reads names and prices
/// from the catalog when it sends (rule 5). Sent like a team reply: takes the conversation over and
/// respects the 24-hour window.
/// </summary>
internal sealed class ProductMessagesHandler(
    InboxDbContext db,
    ICatalog catalog,
    ITeamDirectory team,
    ITenantContext tenant,
    InboxNotifier notifier,
    TimeProvider time)
{
    private static readonly string[] CardButtons = [RichAction.Buy, RichAction.Options, RichAction.Similar];

    public async Task<Result<IReadOnlyList<MessageResponse>>> SendAsync(Guid conversationId, SendProductsRequest request, CancellationToken ct)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is null)
        {
            return InboxErrors.NotFound;
        }

        var now = time.GetUtcNow();
        if (!CustomerWindow.IsOpen(conversation.Channel, conversation.LastInboundAt, now))
        {
            return InboxErrors.OutsideWindow;
        }

        // Check everything before storing anything: all products go, or none.
        var cards = new List<ProductCard>();
        var lines = new StringBuilder();
        foreach (var item in request.Items!)
        {
            var product = await catalog.GetAsync(item.ProductId, ct);
            if (product is null)
            {
                return ProductMessageErrors.ProductNotFound;
            }

            var variant = item.VariantId is { } variantId ? product.Variants.SingleOrDefault(v => v.Id == variantId) : null;
            if (item.VariantId is not null && variant is null)
            {
                return ProductMessageErrors.ProductNotFound;
            }

            if (variant is { Available: false } || (variant is null && !product.Variants.Any(v => v.Available)))
            {
                return ProductMessageErrors.NotAvailable;
            }

            cards.Add(new ProductCard(product.Id, variant?.Id, null, CardButtons));
            lines.Append(CultureInfo.InvariantCulture, $"{cards.Count}. ").Append(CardText(product, variant)).Append('\n');
        }

        var me = await team.CurrentAsync(ct) ?? throw new InvalidOperationException("Inbox endpoints require a team member.");
        // The text is what the team's list and the AI's history read; the customer gets the carousel.
        var message = Message.FromTeam(tenant.TenantId, conversation.Id, me.UserId, me.DisplayName, lines.ToString(), isInternal: false, now);
        message.AttachRich(RichKind.Carousel, RichContentJson.Serialize(new RichContent(Carousel: new ProductCarousel(null, cards))));
        db.Messages.Add(message);
        conversation.RepliedByTeam(message);
        if (conversation.ChannelConnectionId is not null)
        {
            message.QueueDelivery(now);
        }

        await db.SaveChangesAsync(ct);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, ct);
        return Result.Success<IReadOnlyList<MessageResponse>>([ConversationsHandler.ToResponse(message)]);
    }

    /// <summary>"🛍️ Borsa nera — Nero · €89.00".</summary>
    internal static string CardText(CatalogProduct product, CatalogVariant? variant)
    {
        var options = variant is null ? string.Empty : string.Join(" / ", variant.OptionValues.Where(v => !string.IsNullOrWhiteSpace(v)));
        var title = options.Length == 0 ? product.Title : $"{product.Title} — {options}";
        var price = variant?.Price ?? product.Variants.Where(v => v.Available).Min(v => v.Price);
        var currency = variant?.Currency ?? product.Variants.First(v => v.Available).Currency;
        return $"🛍️ {title} · {RichRenderer.Money(price, currency)}";
    }
}
