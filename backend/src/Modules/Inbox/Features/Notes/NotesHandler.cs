using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Features.Conversations;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.Notes;

/// <summary>
/// Team notes (PRD 4.4): written from the side panel, shown in the timeline, pinned, with
/// <c>@mentions</c>. Notes are internal messages, so the channel sender and the AI never see them.
/// </summary>
internal sealed class NotesHandler(
    InboxDbContext db,
    ITeamDirectory team,
    ITenantContext tenant,
    InboxNotifier notifier,
    TimeProvider time)
{
    public async Task<NotesResponse?> ListAsync(Guid conversationId, CancellationToken ct)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == conversationId, ct))
        {
            return null;
        }

        var notes = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId && m.Internal);
        // The side panel keeps notes in time order, pinned or not; pinned notes also sit above the composer.
        var items = await notes
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(NoteValues.ListCount)
            .ToListAsync(ct);
        var pinned = await notes.Where(m => m.PinnedAt != null).OrderBy(m => m.PinnedAt).ToListAsync(ct);

        return new NotesResponse(
            items.Select(ConversationsHandler.ToResponse).ToList(),
            pinned.Select(ConversationsHandler.ToResponse).ToList());
    }

    public async Task<Result<MessageResponse>> SetResolvedAsync(Guid conversationId, Guid noteId, bool resolved, CancellationToken ct)
    {
        var note = await db.Messages.SingleOrDefaultAsync(m => m.Id == noteId && m.ConversationId == conversationId && m.Internal, ct);
        if (note is null)
        {
            return NoteErrors.NotFound;
        }

        if (resolved)
        {
            var me = await team.CurrentAsync(ct) ?? throw new InvalidOperationException("Inbox endpoints require a team member.");
            note.Resolve(me.UserId, time.GetUtcNow());
        }
        else
        {
            note.Reopen();
        }

        await db.SaveChangesAsync(ct);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversationId, ct);
        return Result.Success(ConversationsHandler.ToResponse(note));
    }

    public async Task<Result<MessageResponse>> AddAsync(Guid conversationId, AddNoteRequest request, CancellationToken ct)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == conversationId, ct))
        {
            return InboxErrors.NotFound;
        }

        var me = await team.CurrentAsync(ct) ?? throw new InvalidOperationException("Inbox endpoints require a team member.");
        var mentions = (request.MentionedUserIds ?? []).Distinct().ToList();
        foreach (var userId in mentions)
        {
            if (await team.FindAsync(userId, ct) is null)
            {
                return NoteErrors.MentionNotMember;
            }
        }

        var note = Message.TeamNote(tenant.TenantId, conversationId, me.UserId, me.DisplayName, request.Text, mentions, time.GetUtcNow());
        db.Messages.Add(note);
        await db.SaveChangesAsync(ct);

        await notifier.ConversationChangedAsync(tenant.TenantId, conversationId, ct);
        var others = mentions.Where(id => id != me.UserId).ToList();
        if (others.Count > 0)
        {
            await notifier.MentionedAsync(tenant.TenantId, others, conversationId, note.Id, ct);
        }

        return Result.Success(ConversationsHandler.ToResponse(note));
    }

    public async Task<Result<MessageResponse>> SetPinnedAsync(Guid conversationId, Guid noteId, bool pinned, CancellationToken ct)
    {
        var note = await db.Messages.SingleOrDefaultAsync(m => m.Id == noteId && m.ConversationId == conversationId && m.Internal, ct);
        if (note is null)
        {
            return NoteErrors.NotFound;
        }

        if (pinned && note.ResolvedAt is not null)
        {
            return NoteErrors.Resolved;
        }

        if (pinned && note.PinnedAt is null)
        {
            var count = await db.Messages.CountAsync(m => m.ConversationId == conversationId && m.PinnedAt != null, ct);
            if (count >= NoteValues.MaxPinned)
            {
                return NoteErrors.PinLimit;
            }

            var me = await team.CurrentAsync(ct) ?? throw new InvalidOperationException("Inbox endpoints require a team member.");
            note.Pin(me.UserId, time.GetUtcNow());
        }
        else if (!pinned)
        {
            note.Unpin();
        }

        await db.SaveChangesAsync(ct);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversationId, ct);
        return Result.Success(ConversationsHandler.ToResponse(note));
    }
}
