using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Contacts.Domain;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.Contacts.Infrastructure;

internal sealed class ContactInterests(ContactsDbContext db, ITenantContext tenant) : IContactInterest
{
    public async Task RecordAsync(Guid contactId, Guid productId, string action, DateTimeOffset at, CancellationToken cancellationToken)
    {
        // The query filter and RLS keep this to the current workspace (CLAUDE.md rule 1).
        if (!await db.Contacts.AnyAsync(c => c.Id == contactId, cancellationToken))
        {
            return;
        }

        var existing = await db.ProductInterests
            .SingleOrDefaultAsync(i => i.ContactId == contactId && i.ProductId == productId, cancellationToken);
        if (existing is not null)
        {
            existing.Tapped(action, at);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        db.ProductInterests.Add(ProductInterest.Create(tenant.TenantId, contactId, productId, action, at));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two taps at once: the other one added the row.
            db.ChangeTracker.Clear();
            var added = await db.ProductInterests
                .SingleAsync(i => i.ContactId == contactId && i.ProductId == productId, cancellationToken);
            added.Tapped(action, at);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProductInterestEntry>> ListAsync(Guid contactId, CancellationToken cancellationToken) =>
        await db.ProductInterests
            .AsNoTracking()
            .Where(i => i.ContactId == contactId)
            .OrderByDescending(i => i.LastAt)
            .ThenBy(i => i.Id)
            .Take(IContactInterest.MaxListed)
            .Select(i => new ProductInterestEntry(i.ProductId, i.LastAction, i.Wanted, i.Taps, i.FirstAt, i.LastAt))
            .ToListAsync(cancellationToken);
}
