using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Features.Conversations;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.Products;

/// <summary>
/// "Send products" from the + menu (PRD 4.4, ADR 0024): one message per product, built from the
/// catalog (rule 5). Sent like a team reply: takes the conversation over, respects the 24-hour
/// window, delivered by the channel's sender as text.
/// </summary>
internal sealed class ProductMessagesHandler(
    InboxDbContext db,
    ICatalog catalog,
    ITeamDirectory team,
    ITenantContext tenant,
    InboxNotifier notifier,
    TimeProvider time)
{
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
        var snapshots = new List<ProductSnapshot>();
        foreach (var item in request.Items!)
        {
            var product = await catalog.GetAsync(item.ProductId, ct);
            var variant = product?.Variants.SingleOrDefault(v => v.Id == item.VariantId);
            if (product is null || variant is null)
            {
                return ProductMessageErrors.ProductNotFound;
            }

            if (!variant.Available)
            {
                return ProductMessageErrors.NotAvailable;
            }

            snapshots.Add(new ProductSnapshot
            {
                ProductId = product.Id,
                VariantId = variant.Id,
                Title = product.Title,
                Variant = string.Join(" / ", variant.OptionValues.Where(v => !string.IsNullOrWhiteSpace(v))),
                Sku = variant.Sku,
                Price = variant.Price,
                Currency = variant.Currency,
            });
        }

        var me = await team.CurrentAsync(ct) ?? throw new InvalidOperationException("Inbox endpoints require a team member.");
        var messages = new List<Message>();
        var sentAt = now;
        foreach (var snapshot in snapshots)
        {
            var message = Message.FromTeamProduct(tenant.TenantId, conversation.Id, me.UserId, me.DisplayName, CardText(snapshot), snapshot, sentAt);
            db.Messages.Add(message);
            conversation.RepliedByTeam(message);
            if (conversation.ChannelConnectionId is not null)
            {
                message.QueueDelivery(sentAt);
            }

            messages.Add(message);
            // Keeps the order of the cards, also for the channel.
            sentAt = sentAt.AddMilliseconds(1);
        }

        await db.SaveChangesAsync(ct);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, ct);
        return Result.Success<IReadOnlyList<MessageResponse>>(messages.Select(ConversationsHandler.ToResponse).ToList());
    }

    /// <summary>What the channel shows (ADR 0024: a text card until product images exist).</summary>
    internal static string CardText(ProductSnapshot p)
    {
        var title = string.IsNullOrEmpty(p.Variant) ? p.Title : $"{p.Title} — {p.Variant}";
        var price = Price(p.Price, p.Currency);
        return p.Sku is null ? $"🛍️ {title}\n{price}" : $"🛍️ {title}\n{price} · SKU {p.Sku}";
    }

    /// <summary>8900 EUR → "€89.00"; other currencies "89.00 USD".</summary>
    internal static string Price(long cents, string currency)
    {
        var amount = (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return currency == "EUR" ? $"€{amount}" : $"{amount} {currency}";
    }
}
