using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Features.Products;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

/// <summary>
/// The Inbox side of AI order requests (PRD 4.9, ADR 0025): the team hears about a new request, and
/// the customer hears the decision in the same chat — in their language, from the person who decided,
/// only while the channel allows it (SEC-43). Otherwise the team gets a note to contact them.
/// </summary>
internal sealed class OrderNotifications(
    InboxDbContext db,
    IContactDirectory contacts,
    ITeamDirectory team,
    ITenantContext tenant,
    InboxNotifier notifier,
    TimeProvider time) : IOrderNotifications
{
    public Task RequestCreatedAsync(Guid conversationId, Guid orderId, CancellationToken cancellationToken) =>
        notifier.OrderRequestedAsync(tenant.TenantId, conversationId, orderId, cancellationToken);

    public Task ConfirmedAsync(Guid conversationId, ConfirmedOrderSummary order, CancellationToken cancellationToken) =>
        TellCustomerAsync(conversationId, italian => SummaryText(order, italian), $"order #{order.Number} was confirmed", cancellationToken);

    /// <summary>
    /// The order as the customer reads it (PRD 4.9): lines, shipping, the total to pay on delivery and
    /// the address. Every amount comes from the saved order, which code priced (rule 5).
    /// </summary>
    internal static string SummaryText(ConfirmedOrderSummary order, bool italian)
    {
        string Money(long cents) => RichRenderer.Money(cents, order.Currency);
        var lines = order.Lines.Select(l =>
            $"{l.Quantity}× {(string.IsNullOrEmpty(l.Variant) ? l.Title : $"{l.Title} — {l.Variant}")}  {Money(l.LineTotal)}");
        var shipping = order.ShippingCost == 0 ? (italian ? "gratis" : "free") : Money(order.ShippingCost);
        var text = new List<string>
        {
            italian ? $"Ordine #{order.Number} confermato ✅" : $"Your order #{order.Number} is confirmed ✅",
            string.Empty,
        };
        text.AddRange(lines);
        text.Add(italian ? $"Spedizione ({order.ShippingZoneName})  {shipping}" : $"Shipping ({order.ShippingZoneName})  {shipping}");
        text.Add(italian ? $"Totale da pagare alla consegna: {Money(order.Total)}" : $"Total to pay on delivery: {Money(order.Total)}");
        text.Add(italian
            ? $"Consegna: {order.Street}, {order.PostalCode} {order.City}"
            : $"Delivery: {order.Street}, {order.PostalCode} {order.City}");
        text.Add(string.Empty);
        text.Add(italian ? "Grazie!" : "Thank you!");
        var message = string.Join('\n', text);
        // A message has at most 4,000 characters; 20 lines fit easily, but never fail on it.
        return message.Length <= Message.TextMaxLength ? message : message[..Message.TextMaxLength];
    }

    public Task RejectedAsync(Guid conversationId, int number, CancellationToken cancellationToken) =>
        TellCustomerAsync(
            conversationId,
            italian => italian
                ? $"Ci dispiace, non possiamo confermare l'ordine #{number}. Ti scriviamo a breve per trovare una soluzione."
                : $"We're sorry, we can't confirm order #{number}. We'll get back to you shortly.",
            $"order #{number} was rejected",
            cancellationToken);

    private async Task TellCustomerAsync(Guid conversationId, Func<bool, string> text, string what, CancellationToken ct)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        if (!CustomerWindow.IsOpen(conversation.Channel, conversation.LastInboundAt, now))
        {
            // No message outside the 24-hour window (SEC-43); the WhatsApp template comes later.
            var note = Message.AiNote(tenant.TenantId, conversation.Id,
                $"The customer wasn't told that {what}: the channel's 24-hour window is closed. Please contact them another way.", now);
            db.Messages.Add(note);
            await db.SaveChangesAsync(ct);
            await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, ct);
            return;
        }

        var contact = (await contacts.GetAsync([conversation.ContactId], ct)).GetValueOrDefault(conversation.ContactId);
        var italian = contact?.Language?.StartsWith("it", StringComparison.OrdinalIgnoreCase) ?? true;
        var me = await team.CurrentAsync(ct) ?? throw new InvalidOperationException("Order decisions are made by a team member.");
        var message = Message.FromTeam(tenant.TenantId, conversation.Id, me.UserId, me.DisplayName, text(italian), isInternal: false, now);
        db.Messages.Add(message);
        conversation.OrderUpdateSent(message);
        if (conversation.ChannelConnectionId is not null)
        {
            message.QueueDelivery(now);
        }

        await db.SaveChangesAsync(ct);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, ct);
    }
}
