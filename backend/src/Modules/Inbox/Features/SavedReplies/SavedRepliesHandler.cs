using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.Inbox.Features.SavedReplies;

/// <summary>Saved replies of the current workspace (PRD 4.4, ADR 0022); tenant-scoped like all inbox data (SEC-06).</summary>
internal sealed class SavedRepliesHandler(InboxDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<SavedReplyResponse>> ListAsync(CancellationToken ct) =>
        (await db.SavedReplies.AsNoTracking().OrderBy(r => r.Shortcut).ToListAsync(ct)).Select(ToResponse).ToList();

    public async Task<Result<SavedReplyResponse>> CreateAsync(SavedReplyRequest request, CancellationToken ct)
    {
        if (await db.SavedReplies.CountAsync(ct) >= SavedReply.MaxPerWorkspace)
        {
            return SavedReplyErrors.Limit;
        }

        var reply = SavedReply.Create(tenant.TenantId, request.Shortcut!, request.Title!, request.TextEn, request.TextIt);
        db.SavedReplies.Add(reply);
        return await SaveAsync(reply, ct);
    }

    public async Task<Result<SavedReplyResponse>> UpdateAsync(Guid id, SavedReplyRequest request, CancellationToken ct)
    {
        var reply = await db.SavedReplies.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (reply is null)
        {
            return SavedReplyErrors.NotFound;
        }

        reply.Update(request.Shortcut!, request.Title!, request.TextEn, request.TextIt);
        return await SaveAsync(reply, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var reply = await db.SavedReplies.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (reply is null)
        {
            return Result.Failure(SavedReplyErrors.NotFound);
        }

        db.SavedReplies.Remove(reply);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result<SavedReplyResponse>> SaveAsync(SavedReply reply, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return SavedReplyErrors.ShortcutTaken;
        }

        return Result.Success(ToResponse(reply));
    }

    private static SavedReplyResponse ToResponse(SavedReply r) => new(r.Id, r.Shortcut, r.Title, r.TextEn, r.TextIt, r.UpdatedAt);
}
