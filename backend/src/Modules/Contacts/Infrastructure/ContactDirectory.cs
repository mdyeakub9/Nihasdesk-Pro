using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Contacts.Domain;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.Contacts.Infrastructure;

internal sealed class ContactDirectory(ContactsDbContext db, ITenantContext tenant) : IContactDirectory
{
    public async Task<Guid> FindOrCreateAsync(string channel, string externalId, string? name, CancellationToken cancellationToken)
    {
        if (!ChannelKind.All.Contains(channel))
        {
            throw new ArgumentException($"Unknown channel '{channel}'.", nameof(channel));
        }

        if (await FindAsync(channel, externalId, cancellationToken) is { } existing)
        {
            await SuggestNameAsync(existing, name, cancellationToken);
            return existing;
        }

        var contact = Contact.Create(tenant.TenantId, name);
        db.Contacts.Add(contact);
        db.Identities.Add(ContactIdentity.Create(tenant.TenantId, contact.Id, channel, externalId));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return contact.Id;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two first messages at once: the other one created the contact.
            db.ChangeTracker.Clear();
            return await FindAsync(channel, externalId, cancellationToken)
                ?? throw new InvalidOperationException("Contact identity vanished after a unique violation.", exception);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, ContactSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ContactSummary>();
        }

        return await db.Contacts
            .Where(c => ids.Contains(c.Id))
            .Select(c => new ContactSummary(c.Id, c.Name, c.Phone, c.Email, c.Language, c.CreatedAt))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
    }

    public Task<string?> FindChannelIdAsync(Guid contactId, string channel, CancellationToken cancellationToken) =>
        db.Identities
            .Where(i => i.ContactId == contactId && i.Channel == channel)
            .OrderBy(i => i.CreatedAt)
            .Select(i => i.ExternalId)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<Guid?> FindAsync(string channel, string externalId, CancellationToken cancellationToken) =>
        db.Identities
            .Where(i => i.Channel == channel && i.ExternalId == externalId)
            .Select(i => (Guid?)i.ContactId)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task SuggestNameAsync(Guid contactId, string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var contact = await db.Contacts.SingleAsync(c => c.Id == contactId, cancellationToken);
        if (contact.Name is null)
        {
            contact.SuggestName(name);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
