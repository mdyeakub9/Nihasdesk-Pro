using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.Modules.Inbox.Domain;

/// <summary>
/// What the customer reads on our buttons and in the text fallback, in Italian or English (ADR 0032 §2).
/// Every label fits Messenger's 20 characters.
/// </summary>
internal static class RichLabels
{
    public static string Button(string action, bool italian) => action switch
    {
        RichAction.Buy => italian ? "🛒 Lo voglio" : "🛒 I want it",
        RichAction.Options => italian ? "📏 Taglie e colori" : "📏 Sizes & colours",
        RichAction.Similar => italian ? "🔍 Simili" : "🔍 Similar",
        RichAction.MoreProducts => italian ? "Altri prodotti" : "More products",
        RichAction.TalkToHuman => italian ? "Parla con noi" : "Talk to a person",
        RichAction.ConfirmSummary => italian ? "✅ Conferma" : "✅ Confirm",
        RichAction.EditSummary => italian ? "✏️ Modifica" : "✏️ Change",
        _ => throw new ArgumentException($"No standard label for '{action}'.", nameof(action)),
    };

    public static string From(bool italian) => italian ? "da" : "from";

    public static string SoldOut(bool italian) => italian ? "Esaurito" : "Sold out";

    public static string Similar(bool italian) => italian ? "Simile" : "Similar";

    public static string PickProduct(bool italian) =>
        italian ? "Rispondi con il numero del prodotto che ti interessa." : "Reply with the number of the product you like.";

    public static string PickOption(bool italian) => italian ? "Rispondi con il numero." : "Reply with the number.";

    public static string NoLongerAvailable(bool italian) =>
        italian ? "Questi prodotti non sono più disponibili." : "These products are no longer available.";
}
