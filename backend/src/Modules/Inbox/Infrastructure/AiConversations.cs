using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

internal sealed class AiConversations(
    InboxDbContext db,
    ITenantContext tenant,
    InboxNotifier notifier,
    TimeProvider time) : IAiConversations
{
    public async Task<IReadOnlyList<DueAiReply>> DueAsync(int limit, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        // SECURITY DEFINER lookup (migration AddAiReplies): IDs only, no message content.
        var rows = await db.Database
            .SqlQuery<DueRow>($"SELECT tenant_id, conversation_id FROM inbox.due_ai_replies({now}, {limit})")
            .ToListAsync(cancellationToken);
        return rows.Select(r => new DueAiReply(r.TenantId, r.ConversationId)).ToList();
    }

    public async Task<AiConversation?> ClaimAsync(Guid conversationId, int historySize, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT id AS value FROM inbox.conversations WHERE id = {conversationId} AND ai_reply_due_at <= {now} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        if (locked.Count == 0)
        {
            return null;
        }

        var conversation = await db.Conversations.SingleAsync(c => c.Id == conversationId, cancellationToken);
        conversation.AiReplyStarted();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (!conversation.AiAnswers || !CustomerWindow.IsOpen(conversation.Channel, conversation.LastInboundAt, now))
        {
            return null;
        }

        return await ContextAsync(conversation, historySize, cancellationToken);
    }

    public async Task<AiConversation?> HistoryAsync(Guid conversationId, int historySize, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        return conversation is null ? null : await ContextAsync(conversation, historySize, cancellationToken);
    }

    /// <summary>What the AI may see: never internal notes (CLAUDE.md rule 7, SEC-37), never team names.</summary>
    private async Task<AiConversation> ContextAsync(Conversation conversation, int historySize, CancellationToken cancellationToken)
    {
        var history = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id && !m.Internal)
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(historySize)
            .Select(m => new { m.Id, m.Author, m.Text, m.SentAt, HasMedia = db.Attachments.Any(a => a.MessageId == m.Id) })
            .ToListAsync(cancellationToken);
        var aiHasReplied = await db.Messages.AnyAsync(m => m.ConversationId == conversation.Id && m.Author == MessageAuthor.Ai && !m.Internal, cancellationToken);

        return new AiConversation(
            conversation.Id,
            conversation.ContactId,
            conversation.Channel,
            aiHasReplied,
            history.AsEnumerable().Reverse().Select(m => new AiHistoryMessage(
                m.Author.ToString().ToLowerInvariant(),
                m.Text.Length > 0 ? m.Text : AiHistoryMessage.MediaOnlyText,
                m.SentAt,
                m.Id,
                m.HasMedia)).ToList());
    }

    public async Task<bool> ReplyAsync(Guid conversationId, IReadOnlyList<string> messages, CancellationToken cancellationToken) =>
        await ReplyWithIdsAsync(conversationId, messages, cancellationToken) is not null;

    public async Task<IReadOnlyList<Guid>?> ReplyWithIdsAsync(Guid conversationId, IReadOnlyList<string> messages, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        var now = time.GetUtcNow();
        // The team may have taken over while the AI was thinking: then the AI stays silent.
        if (conversation is null || !conversation.AiAnswers || !CustomerWindow.IsOpen(conversation.Channel, conversation.LastInboundAt, now))
        {
            return null;
        }

        var ids = new List<Guid>();
        var sentAt = now;
        foreach (var text in messages)
        {
            var message = Message.FromAi(tenant.TenantId, conversation.Id, text, sentAt);
            db.Messages.Add(message);
            ids.Add(message.Id);
            conversation.RepliedByAi(message);
            if (conversation.ChannelConnectionId is not null)
            {
                message.QueueDelivery(sentAt);
            }

            // Keeps the order of disclosure and answer, also for the channel.
            sentAt = sentAt.AddMilliseconds(1);
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, cancellationToken);
        return ids;
    }

    public async Task HandOffAsync(Guid conversationId, string teamNote, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return;
        }

        conversation.HandOffToTeam();
        db.Messages.Add(Message.AiNote(tenant.TenantId, conversation.Id, teamNote, time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, cancellationToken);
    }

    private sealed record DueRow(Guid TenantId, Guid ConversationId);
}
