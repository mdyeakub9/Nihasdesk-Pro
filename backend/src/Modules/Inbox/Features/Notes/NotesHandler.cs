using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Features.Conversations;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.Notes;

/// <summary>
/// Team notes (PRD 4.4, ADR 0021): written, resolved and deleted in the side panel's Notes section,
/// with <c>@mentions</c>. They are internal messages, so the channel sender and the AI never see
/// them. The AI's own handoff notes stay in the conversation and are not team notes.
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

        var items = await TeamNotes(conversationId)
            .AsNoTracking()
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(NoteValues.ListCount)
            .ToListAsync(ct);

        return new NotesResponse(items.Select(ConversationsHandler.ToResponse).ToList());
    }

    public async Task<Result<MessageResponse>> AddAsync(Guid conversationId, AddNoteRequest request, CancellationToken ct)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == conversationId, ct))
        {
            return InboxErrors.NotFound;
        }

        var me = await MeAsync(ct);
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

    public async Task<Result<MessageResponse>> SetResolvedAsync(Guid conversationId, Guid noteId, bool resolved, CancellationToken ct)
    {
        var note = await TeamNotes(conversationId).SingleOrDefaultAsync(m => m.Id == noteId, ct);
        if (note is null)
        {
            return NoteErrors.NotFound;
        }

        if (resolved)
        {
            note.Resolve((await MeAsync(ct)).UserId, time.GetUtcNow());
        }
        else
        {
            note.Reopen();
        }

        await db.SaveChangesAsync(ct);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversationId, ct);
        return Result.Success(ConversationsHandler.ToResponse(note));
    }

    /// <summary>The note's author, or an Owner or Admin (docs/standards/permissions.md), deletes it for good.</summary>
    public async Task<Result> DeleteAsync(Guid conversationId, Guid noteId, CancellationToken ct)
    {
        var note = await TeamNotes(conversationId).SingleOrDefaultAsync(m => m.Id == noteId, ct);
        if (note is null)
        {
            return Result.Failure(NoteErrors.NotFound);
        }

        var me = await MeAsync(ct);
        if (note.AuthorUserId != me.UserId && me.Role is not ("owner" or "admin"))
        {
            return Result.Failure(NoteErrors.NotAuthor);
        }

        db.Messages.Remove(note);
        await db.SaveChangesAsync(ct);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversationId, ct);
        return Result.Success();
    }

    /// <summary>Notes a team member wrote in this conversation; not the AI's handoff notes (SEC-06: tenant-scoped).</summary>
    private IQueryable<Message> TeamNotes(Guid conversationId) =>
        db.Messages.Where(m => m.ConversationId == conversationId && m.Internal && m.Author == MessageAuthor.Agent);

    private async Task<TeamMemberInfo> MeAsync(CancellationToken ct) =>
        await team.CurrentAsync(ct) ?? throw new InvalidOperationException("Inbox endpoints require a team member.");
}
