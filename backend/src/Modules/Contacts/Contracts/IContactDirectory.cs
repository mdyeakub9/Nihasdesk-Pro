namespace Nihasdesk.Modules.Contacts.Contracts;

/// <summary>Channels a contact can be reached on (PRD 4.3; <c>website</c> is the chat widget). Stored as this text.</summary>
public static class ChannelKind
{
    public const string Website = "website";

    public const string Messenger = "messenger";

    public const string Instagram = "instagram";

    public const string WhatsApp = "whatsapp";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Website, Messenger, Instagram, WhatsApp,
    };
}

/// <summary>What other modules may read about a contact.</summary>
public sealed record ContactSummary(
    Guid Id,
    string? Name,
    string? Phone,
    string? Email,
    string? Language,
    DateTimeOffset CreatedAt);

/// <summary>Contacts of the current workspace (tenant from the request or job, CLAUDE.md rule 1).</summary>
public interface IContactDirectory
{
    /// <summary>
    /// The contact behind a channel address, created on first contact (PRD 4.5). <paramref name="name"/>
    /// only fills an empty name; it never overwrites what the team entered.
    /// </summary>
    Task<Guid> FindOrCreateAsync(string channel, string externalId, string? name, CancellationToken cancellationToken);

    /// <summary>Contacts by ID; IDs of other workspaces are simply missing from the result.</summary>
    Task<IReadOnlyDictionary<Guid, ContactSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
