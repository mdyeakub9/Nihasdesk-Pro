using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

/// <summary>
/// SEC-45 for media. The tenant-scoped context (query filter and RLS) keeps it inside the current
/// workspace. Files are deleted before their rows, in small batches, so a failure can be run again.
/// </summary>
internal sealed class MediaErasure(InboxDbContext db, IFileStore files) : IMediaErasure
{
    private const int BatchSize = 100;

    public Task<int> EraseContactAsync(Guid contactId, CancellationToken cancellationToken) =>
        EraseAsync(
            db.Attachments.Where(a => db.Messages.Any(m =>
                m.Id == a.MessageId && db.Conversations.Any(c => c.Id == m.ConversationId && c.ContactId == contactId))),
            cancellationToken);

    public Task<int> EraseWorkspaceAsync(CancellationToken cancellationToken) =>
        EraseAsync(db.Attachments, cancellationToken);

    private async Task<int> EraseAsync(IQueryable<MessageAttachment> attachments, CancellationToken ct)
    {
        var erased = 0;
        while (true)
        {
            var batch = await attachments.OrderBy(a => a.Id).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0)
            {
                return erased;
            }

            foreach (var key in batch.SelectMany(a => a.StoredKeys()))
            {
                await files.DeleteAsync(key, ct);
            }

            db.Attachments.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            erased += batch.Count;
        }
    }
}
