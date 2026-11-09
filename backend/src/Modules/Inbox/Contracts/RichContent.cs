namespace Nihasdesk.Modules.Inbox.Contracts;

/// <summary>
/// Interactive content sent with a message (ADR 0032 §1), the same for every channel. It holds
/// product <b>ids</b>, never titles or prices: the channel sender reads those from the catalog at
/// send time (CLAUDE.md rule 5, SEC-41). Exactly one of the parts is set.
/// </summary>
public sealed record RichContent(
    ProductCarousel? Carousel = null,
    QuickReplies? QuickReplies = null,
    ButtonMessage? Buttons = null)
{
    public string Kind => Carousel is not null ? RichKind.Carousel
        : QuickReplies is not null ? RichKind.QuickReplies
        : RichKind.Buttons;
}

public static class RichKind
{
    public const string Carousel = "carousel";
    public const string QuickReplies = "quick_replies";
    public const string Buttons = "buttons";
}

/// <summary>1 to 10 product cards the customer swipes.</summary>
public sealed record ProductCarousel(string? Intro, IReadOnlyList<ProductCard> Cards)
{
    public const int MaxCards = 10;
}

/// <param name="MatchLabel">One of <see cref="MatchLabel"/> or null.</param>
/// <param name="Buttons">Up to 3 card buttons, in order.</param>
public sealed record ProductCard(Guid ProductId, Guid? VariantId, string? MatchLabel, IReadOnlyList<string> Buttons)
{
    public const int MaxButtons = 3;
}

public static class MatchLabel
{
    public const string Likely = "likely";
    public const string Similar = "similar";
}

/// <summary>A question with 1 to 13 short choices; they disappear after a tap.</summary>
public sealed record QuickReplies(string Text, IReadOnlyList<RichOption> Options)
{
    public const int MaxOptions = 13;
}

/// <summary>Text with 1 to 3 buttons, e.g. Conferma / Modifica under the order summary.</summary>
public sealed record ButtonMessage(string Text, IReadOnlyList<RichOption> Buttons)
{
    public const int TextMaxLength = 640;
    public const int MaxButtons = 3;
}

/// <summary>
/// One choice. <paramref name="Label"/> is what the customer sees (≤ 20 characters);
/// <paramref name="Action"/> is one of <see cref="RichAction"/>. Product, variant and value are what
/// the tap acts on; they are kept on the server and never sent in the payload (ADR 0032 §3).
/// </summary>
public sealed record RichOption(
    string Label,
    string Action,
    Guid? ProductId = null,
    Guid? VariantId = null,
    string? Value = null)
{
    public const int LabelMaxLength = 20;
    public const int ValueMaxLength = 100;
}

/// <summary>The closed set of things a button or quick reply can do (ADR 0032 §1).</summary>
public static class RichAction
{
    public const string Buy = "buy";
    public const string Options = "options";
    public const string Similar = "similar";
    public const string PickVariant = "pick_variant";
    public const string ConfirmSummary = "confirm_summary";
    public const string EditSummary = "edit_summary";
    public const string MoreProducts = "more_products";
    public const string TalkToHuman = "talk_to_human";
    public const string OpenForm = "open_form";

    /// <summary>A free choice from <c>show_choices</c> (e.g. a delivery zone): the label is the customer's answer, for the AI.</summary>
    public const string Choose = "choose";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Buy, Options, Similar, PickVariant, ConfirmSummary, EditSummary, MoreProducts, TalkToHuman, OpenForm, Choose,
    };

    /// <summary>Taps code answers even while the team has the conversation (ADR 0032 §7).</summary>
    public static readonly IReadOnlySet<string> CodeAnswersForTeam = new HashSet<string>(StringComparer.Ordinal)
    {
        Options, PickVariant,
    };

    /// <summary>Buttons a product card may have.</summary>
    public static readonly IReadOnlySet<string> CardButtons = new HashSet<string>(StringComparer.Ordinal)
    {
        Buy, Options, Similar,
    };
}
