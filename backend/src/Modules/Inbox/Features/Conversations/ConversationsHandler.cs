using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.Inbox.Features.Conversations;

/// <summary>
/// Inbox work for every team member (docs/standards/permissions.md). All reads go through the
/// tenant-scoped context, so another workspace's IDs are simply not found (SEC-06).
/// </summary>
internal sealed class ConversationsHandler(
    InboxDbContext db,
    IContactDirectory contacts,
    ITeamDirectory team,
    ITenantContext tenant,
    InboxNotifier notifier,
    TimeProvider time)
{
    public async Task<ConversationPageResponse> ListAsync(ListConversationsQuery query, CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var conversations = db.Conversations.AsNoTracking();

        if (InboxValues.TryParse<ConversationStatus>(query.Status, out var status))
        {
            conversations = conversations.Where(c => c.Status == status);
        }

        if (query.Channel is { } channel)
        {
            conversations = conversations.Where(c => c.Channel == channel);
        }

        if (InboxValues.TryParse<Responder>(query.Responder, out var responder))
        {
            conversations = conversations.Where(c => c.Responder == responder);
        }

        conversations = query.Assignee switch
        {
            null => conversations,
            "me" => conversations.Where(c => c.AssigneeId == me.UserId),
            "unassigned" => conversations.Where(c => c.AssigneeId == null),
            var id => conversations.Where(c => c.AssigneeId == Guid.Parse(id)),
        };

        if (PageCursor.TryDecode(query.Cursor, out var cursor))
        {
            conversations = conversations.Where(c =>
                EF.Functions.LessThan(ValueTuple.Create(c.LastMessageAt, c.Id), ValueTuple.Create(cursor.At, cursor.Id)));
        }

        var rows = WithReadAt(conversations, me.UserId);
        if (query.Unread is { } unread)
        {
            rows = unread
                ? rows.Where(r => r.Conversation.LastInboundAt != null && (r.ReadAt == null || r.ReadAt < r.Conversation.LastInboundAt))
                : rows.Where(r => r.Conversation.LastInboundAt == null || (r.ReadAt != null && r.ReadAt >= r.Conversation.LastInboundAt));
        }

        var limit = query.Limit ?? InboxValues.DefaultPageSize;
        var page = await rows
            .OrderByDescending(r => r.Conversation.LastMessageAt)
            .ThenByDescending(r => r.Conversation.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        var items = page.Take(limit).ToList();
        var names = await contacts.GetAsync(items.Select(r => r.Conversation.ContactId).Distinct().ToList(), ct);
        var next = page.Count > limit ? PageCursor.Encode(items[^1].Conversation.LastMessageAt, items[^1].Conversation.Id) : null;

        return new ConversationPageResponse(
            items.Select(r => ToSummary(r, names.GetValueOrDefault(r.Conversation.ContactId)?.Name)).ToList(),
            next);
    }

    public async Task<ConversationResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var row = await WithReadAt(db.Conversations.AsNoTracking().Where(c => c.Id == id), me.UserId).SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        var contact = (await contacts.GetAsync([row.Conversation.ContactId], ct)).GetValueOrDefault(row.Conversation.ContactId);
        return new ConversationResponse(ToSummary(row, contact?.Name), contact);
    }

    public async Task<MessagePageResponse?> MessagesAsync(Guid conversationId, ListMessagesQuery query, CancellationToken ct)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == conversationId, ct))
        {
            return null;
        }

        var messages = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        if (PageCursor.TryDecode(query.Before, out var before))
        {
            messages = messages.Where(m =>
                EF.Functions.LessThan(ValueTuple.Create(m.SentAt, m.Id), ValueTuple.Create(before.At, before.Id)));
        }

        var limit = query.Limit ?? InboxValues.DefaultPageSize;
        var page = await messages
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        var items = page.Take(limit).Reverse().ToList();
        var older = page.Count > limit ? PageCursor.Encode(items[0].SentAt, items[0].Id) : null;
        return new MessagePageResponse(items.Select(ToResponse).ToList(), older);
    }

    /// <summary>A reply to the customer. Team notes have their own endpoints (Features/Notes).</summary>
    public async Task<Result<MessageResponse>> ReplyAsync(Guid conversationId, SendMessageRequest request, CancellationToken ct)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is null)
        {
            return InboxErrors.NotFound;
        }

        var now = time.GetUtcNow();
        if (!CustomerWindow.IsOpen(conversation.Channel, conversation.LastInboundAt, now))
        {
            return InboxErrors.OutsideWindow;
        }

        var me = await MeAsync(ct);
        var message = Message.FromTeam(tenant.TenantId, conversation.Id, me.UserId, me.DisplayName, request.Text, isInternal: false, now);
        db.Messages.Add(message);
        conversation.RepliedByTeam(message);
        if (conversation.ChannelConnectionId is not null)
        {
            // Sent by the channel's background sender (ADR 0017).
            message.QueueDelivery(now);
        }

        await db.SaveChangesAsync(ct);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, ct);
        return Result.Success(ToResponse(message));
    }

    public async Task<Result<ConversationResponse>> UpdateAsync(Guid id, UpdateConversationRequest request, CancellationToken ct)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
        {
            return InboxErrors.NotFound;
        }

        if (InboxValues.TryParse<Responder>(request.Responder, out var responder))
        {
            if (responder == Responder.Human)
            {
                conversation.TakeOver();
            }
            else
            {
                conversation.ReturnToAi();
            }
        }

        // After the responder, so an explicit status wins over "return to AI".
        if (InboxValues.TryParse<ConversationStatus>(request.Status, out var status))
        {
            conversation.SetStatus(status);
        }

        if (request.AiEnabled is { } aiEnabled)
        {
            conversation.SetAiEnabled(aiEnabled);
        }

        if (InboxValues.TryParse<LeadStatus>(request.LeadStatus, out var leadStatus))
        {
            conversation.SetLeadStatus(leadStatus);
        }

        return await SaveAndLoadAsync(conversation, ct);
    }

    public async Task<Result<ConversationResponse>> AssignAsync(Guid id, AssignRequest request, CancellationToken ct)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
        {
            return InboxErrors.NotFound;
        }

        if (request.UserId is { } userId && await team.FindAsync(userId, ct) is null)
        {
            return InboxErrors.AssigneeNotMember;
        }

        conversation.AssignTo(request.UserId);
        return await SaveAndLoadAsync(conversation, ct);
    }

    public async Task<Result> MarkReadAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == id, ct))
        {
            return Result.Failure(InboxErrors.NotFound);
        }

        var me = await MeAsync(ct);
        var now = time.GetUtcNow();
        var marker = await db.ReadMarkers.SingleOrDefaultAsync(r => r.ConversationId == id && r.UserId == me.UserId, ct);
        if (marker is null)
        {
            db.ReadMarkers.Add(ReadMarker.Create(tenant.TenantId, id, me.UserId, now));
        }
        else
        {
            marker.MarkRead(now);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two tabs marked it read at once; one marker is enough.
        }

        return Result.Success();
    }

    private async Task<Result<ConversationResponse>> SaveAndLoadAsync(Conversation conversation, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, ct);
        return Result.Success((await GetAsync(conversation.Id, ct))!);
    }

    /// <summary>The endpoint filter already checked membership; this returns the cached result.</summary>
    private async Task<TeamMemberInfo> MeAsync(CancellationToken ct) =>
        await team.CurrentAsync(ct) ?? throw new InvalidOperationException("Inbox endpoints require a team member.");

    private IQueryable<ConversationRow> WithReadAt(IQueryable<Conversation> conversations, Guid userId) =>
        conversations.Select(c => new ConversationRow
        {
            Conversation = c,
            ReadAt = db.ReadMarkers
                .Where(r => r.ConversationId == c.Id && r.UserId == userId)
                .Select(r => (DateTimeOffset?)r.ReadAt)
                .FirstOrDefault(),
        });

    private static ConversationSummaryResponse ToSummary(ConversationRow row, string? contactName)
    {
        var c = row.Conversation;
        var unread = c.LastInboundAt is { } inbound && (row.ReadAt is null || row.ReadAt < inbound);
        return new ConversationSummaryResponse(
            c.Id,
            c.ContactId,
            contactName,
            c.Channel,
            InboxValues.Text(c.Status),
            InboxValues.Text(c.Responder),
            c.AiEnabled,
            InboxValues.Text(c.LeadStatus),
            c.AssigneeId,
            unread,
            c.Preview,
            c.LastMessageAt);
    }

    internal static MessageResponse ToResponse(Message m) =>
        new(
            m.Id,
            InboxValues.Text(m.Author),
            m.AuthorUserId,
            m.AuthorName,
            m.Text,
            m.Internal,
            m.SentAt,
            InboxValues.Text(m.Delivery),
            m.PinnedAt,
            m.MentionedUserIds,
            m.ResolvedAt,
            m.ResolvedByUserId);

    private sealed class ConversationRow
    {
        public required Conversation Conversation { get; init; }

        public DateTimeOffset? ReadAt { get; init; }
    }
}
