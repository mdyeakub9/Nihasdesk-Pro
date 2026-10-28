using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Contacts.Domain;

/// <summary>One person who wrote to the workspace, across all their channels (PRD 4.5).</summary>
internal sealed class Contact : TenantEntity
{
    public const int NameMaxLength = 200;
    public const int PhoneMaxLength = 50;
    public const int EmailMaxLength = 254;
    public const int LanguageMaxLength = 10;
    public const int NotesMaxLength = 2000;

    private Contact(Guid tenantId, string? name)
        : base(tenantId)
    {
        Name = name;
    }

    public string? Name { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? Language { get; private set; }

    public string? Notes { get; private set; }

    public static Contact Create(Guid tenantId, string? name) => new(tenantId, Clean(name, NameMaxLength));

    /// <summary>Fills the name from the channel profile only when the team hasn't set one.</summary>
    public void SuggestName(string? name)
    {
        Name ??= Clean(name, NameMaxLength);
    }

    public void Update(string? name, string? phone, string? email, string? language, string? notes)
    {
        Name = Clean(name, NameMaxLength);
        Phone = Clean(phone, PhoneMaxLength);
        Email = Clean(email, EmailMaxLength);
        Language = Clean(language, LanguageMaxLength);
        Notes = Clean(notes, NotesMaxLength);
    }

    private static string? Clean(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

/// <summary>A contact's address on one channel, e.g. a Messenger PSID or a widget visitor ID.</summary>
internal sealed class ContactIdentity : TenantEntity
{
    public const int ChannelMaxLength = 20;
    public const int ExternalIdMaxLength = 200;

    private ContactIdentity(Guid tenantId, Guid contactId, string channel, string externalId)
        : base(tenantId)
    {
        ContactId = contactId;
        Channel = channel;
        ExternalId = externalId;
    }

    public Guid ContactId { get; private set; }

    public string Channel { get; private set; }

    public string ExternalId { get; private set; }

    public static ContactIdentity Create(Guid tenantId, Guid contactId, string channel, string externalId)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("ContactId is required.", nameof(contactId));
        }

        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > ExternalIdMaxLength)
        {
            throw new ArgumentException("External ID is required and at most 200 characters.", nameof(externalId));
        }

        return new ContactIdentity(tenantId, contactId, channel, externalId);
    }
}
