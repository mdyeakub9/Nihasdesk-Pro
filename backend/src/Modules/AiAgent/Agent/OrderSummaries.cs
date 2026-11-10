using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>A summary <c>quote_order</c> checked, waiting to go out with the reply (ADR 0032 §5).</summary>
internal sealed record OrderSummaryToSend(IReadOnlyList<OrderRequestLine> Lines, OrderQuote Quote, OrderRequestDetails Details);

/// <summary>
/// The order summary with Conferma / Modifica (PRD 4.9 steps 4–5), written by code from the quote —
/// never by the model (rule 5). Its hash covers items, prices, shipping and address: a Conferma whose
/// hash no longer matches answers with a fresh summary instead of creating anything.
/// </summary>
internal static class OrderSummaries
{
    /// <summary>SHA-256 (hex) of everything the customer confirms.</summary>
    public static string Hash(OrderQuote quote, OrderRequestDetails details)
    {
        var text = new StringBuilder();
        foreach (var line in quote.Lines.OrderBy(l => l.VariantId))
        {
            text.Append(CultureInfo.InvariantCulture, $"{line.ProductId}|{line.VariantId}|{line.Quantity}|{line.UnitPrice}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"{details.ShippingZoneId}|{quote.ShippingCost}|{quote.Subtotal}|{quote.Total}|{quote.Currency}\n");
        text.Append(string.Join('|', details.CustomerName, details.Phone, details.Street, details.PostalCode, details.City, details.Province, details.Country, details.Notes));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>The summary, then the buttons. One button message when it fits Messenger's 640 characters.</summary>
    public static IReadOnlyList<AiReply> Replies(OrderQuote quote, OrderRequestDetails details, string hash, bool italian, string? lead = null)
    {
        var summary = Text(quote, details, italian);
        if (lead is not null)
        {
            summary = $"{lead}\n{summary}";
        }

        RichOption[] buttons =
        [
            new(italian ? "✅ Conferma" : "✅ Confirm", RichAction.ConfirmSummary, Value: hash),
            new(italian ? "✏️ Modifica" : "✏️ Change", RichAction.EditSummary),
        ];
        if (summary.Length <= ButtonMessage.TextMaxLength)
        {
            return [new AiReply(summary, new RichContent(Buttons: new ButtonMessage(summary, buttons)))];
        }

        var question = italian ? "Confermi l'ordine?" : "Do you confirm the order?";
        return [new AiReply(summary), new AiReply(question, new RichContent(Buttons: new ButtonMessage(question, buttons)))];
    }

    public static string Text(OrderQuote quote, OrderRequestDetails details, bool italian)
    {
        var text = new StringBuilder(italian ? "🧾 Riepilogo dell'ordine\n" : "🧾 Order summary\n");
        foreach (var line in quote.Lines)
        {
            var name = string.IsNullOrEmpty(line.Variant) ? line.Title : $"{line.Title} — {line.Variant}";
            text.Append(CultureInfo.InvariantCulture, $"• {line.Quantity} × {name}: {Money(line.UnitPrice * line.Quantity, quote.Currency)}\n");
        }

        var shipping = quote.ShippingCost == 0 ? (italian ? "gratuita" : "free") : Money(quote.ShippingCost, quote.Currency);
        text.Append(italian ? $"Spedizione ({quote.ShippingZoneName}): {shipping}\n" : $"Shipping ({quote.ShippingZoneName}): {shipping}\n");
        text.Append(italian
            ? $"Totale da pagare alla consegna: {Money(quote.Total, quote.Currency)}\n"
            : $"Total, cash on delivery: {Money(quote.Total, quote.Currency)}\n");
        var province = string.IsNullOrWhiteSpace(details.Province) ? string.Empty : $" ({details.Province})";
        text.Append(italian ? "Consegna a: " : "Deliver to: ")
            .Append(CultureInfo.InvariantCulture, $"{details.CustomerName}, {details.Street}, {details.PostalCode} {details.City}{province}, {details.Country}\n");
        text.Append(italian ? "Telefono: " : "Phone: ").Append(details.Phone);
        return text.ToString();
    }

    public static DraftSummary ToDraft(OrderRequestDetails d) =>
        new(d.ShippingZoneId, d.CustomerName, d.Phone, d.Street, d.PostalCode, d.City, d.Province, d.Country, d.Notes);

    public static OrderRequestDetails FromDraft(DraftSummary s) =>
        new(s.CustomerName, s.Phone, s.Street, s.PostalCode, s.City, s.Province, s.Country, s.ZoneId, s.Notes);

    public static string Money(long cents, string currency)
    {
        var amount = (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return currency == "EUR" ? $"€{amount}" : $"{amount} {currency}";
    }
}
