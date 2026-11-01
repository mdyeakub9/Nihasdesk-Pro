using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Domain;

/// <summary>
/// A ready-made answer the team inserts with <c>/</c> (PRD 4.4, ADR 0022). English and Italian
/// versions; the composer picks the customer's language. Inserting never sends it.
/// </summary>
internal sealed class SavedReply : TenantEntity
{
    public const int ShortcutMaxLength = 30;
    public const int TitleMaxLength = 80;
    public const int TextMaxLength = 2000;
    public const int MaxPerWorkspace = 200;

    private SavedReply(Guid tenantId, string shortcut, string title)
        : base(tenantId)
    {
        Shortcut = shortcut;
        Title = title;
    }

    /// <summary>Lower-case letters, digits and dashes, e.g. <c>ship</c> for <c>/ship</c>.</summary>
    public string Shortcut { get; private set; }

    public string Title { get; private set; }

    public string? TextEn { get; private set; }

    public string? TextIt { get; private set; }

    public static SavedReply Create(Guid tenantId, string shortcut, string title, string? textEn, string? textIt)
    {
        var reply = new SavedReply(tenantId, shortcut, title);
        reply.Update(shortcut, title, textEn, textIt);
        return reply;
    }

    public void Update(string shortcut, string title, string? textEn, string? textIt)
    {
        Shortcut = shortcut.Trim().ToLowerInvariant();
        Title = title.Trim();
        TextEn = Clean(textEn);
        TextIt = Clean(textIt);
        if (TextEn is null && TextIt is null)
        {
            throw new ArgumentException("A saved reply needs an English or an Italian text.");
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
