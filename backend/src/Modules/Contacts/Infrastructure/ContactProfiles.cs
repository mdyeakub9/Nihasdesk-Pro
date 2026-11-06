using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Contacts.Contracts;

namespace Nihasdesk.Modules.Contacts.Infrastructure;

internal sealed class ContactProfiles(ContactsDbContext db) : IContactProfile
{
    private const int MaxFirstNameLength = 40;

    public async Task<ContactProfile?> GetAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts
            .AsNoTracking()
            .Where(c => c.Id == contactId)
            .Select(c => new { c.Name, c.Language })
            .SingleOrDefaultAsync(cancellationToken);
        return contact is null ? null : new ContactProfile(FirstName(contact.Name), contact.Language);
    }

    private static string? FirstName(string? name) =>
        name?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { Length: <= MaxFirstNameLength } first ? first : null;
}
