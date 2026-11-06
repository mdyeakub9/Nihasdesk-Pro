using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

internal sealed class AiMessageReader(InboxDbContext db) : IAiMessageReader
{
    /// <summary>Never internal notes (CLAUDE.md rule 7, SEC-37), never team names.</summary>
    public async Task<IReadOnlyList<AiHistoryMessage>> AfterAsync(
        Guid conversationId, DateTimeOffset? afterSentAt, Guid? afterId, int limit, CancellationToken cancellationToken)
    {
        var query = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId && !m.Internal);
        if (afterSentAt is { } at)
        {
            // Messages can share a time: the order below decides, so the given one is skipped after reading.
            query = query.Where(m => m.SentAt >= at);
        }

        var rows = await query
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .Take(limit)
            .Select(m => new { m.Id, m.Author, m.Text, m.SentAt })
            .ToListAsync(cancellationToken);
        var after = rows.FindIndex(m => m.Id == afterId);
        return rows.Skip(after + 1).Select(m => new AiHistoryMessage(m.Author.ToString().ToLowerInvariant(), m.Text, m.SentAt, m.Id)).ToList();
    }
}
