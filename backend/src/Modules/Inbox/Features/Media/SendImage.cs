using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Media;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Features.Conversations;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.Media;

internal static class SendImageErrors
{
    public static readonly Error NoFile = Error.Validation("media.no_file", "Choose a photo to send.");

    public static readonly Error TooLarge = Error.Validation("media.too_large", "The photo is larger than the limit.");

    /// <summary>Not JPEG, PNG, WebP or GIF by its content, or it can't be opened (SEC-04).</summary>
    public static readonly Error TypeNotAllowed = Error.Validation("media.type_not_allowed", "Send a JPEG, PNG, WebP or GIF photo.");
}

/// <summary>
/// <c>POST /api/v1/conversations/{id}/images</c> (ADR 0031 §6): a team member sends a photo from the
/// + menu. Checked like customer media (type by content, re-encoded, no EXIF or GPS), only inside the
/// 24-hour window (SEC-43), then sent by the channel's sender like any team reply.
/// </summary>
internal static class SendImageEndpoints
{
    /// <summary>
    /// A hard cap at the server; the configured photo limit (<see cref="InboxMediaOptions.ImageMaxBytes"/>,
    /// 10 MB) is checked by the handler. Room for the multipart envelope included.
    /// </summary>
    private const long RequestMaxBytes = (25 * 1024 * 1024) + (64 * 1024);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // The form is read by hand (not bound to IFormFile), so the API's own CSRF check applies (SEC-03).
        endpoints.MapPost("/api/v1/conversations/{id:guid}/images", async (Guid id, HttpRequest request, SendImageHandler handler, CancellationToken ct) =>
            {
                var result = await handler.SendAsync(id, request, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/v1/conversations/{id}/messages", result.Value)
                    : result.Error.ToProblem();
            })
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>()
            .WithMetadata(new RequestSizeLimitAttribute(RequestMaxBytes))
            .RequireRateLimiting(RatePolicies.Uploads);
    }
}

internal sealed class SendImageHandler(
    InboxDbContext db,
    IFileStore files,
    ITeamDirectory team,
    ITenantContext tenant,
    InboxNotifier notifier,
    IOptions<InboxMediaOptions> options,
    TimeProvider time)
{
    public async Task<Result<MessageResponse>> SendAsync(Guid conversationId, HttpRequest request, CancellationToken ct)
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

        if (!request.HasFormContentType)
        {
            return SendImageErrors.NoFile;
        }

        var form = await request.ReadFormAsync(ct);
        if (form.Files.GetFile("file") is not { Length: > 0 } file)
        {
            return SendImageErrors.NoFile;
        }

        var limit = options.Value.ImageMaxBytes;
        if (file.Length > limit)
        {
            return SendImageErrors.TooLarge;
        }

        byte[] content;
        using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer, ct);
            content = buffer.ToArray();
        }

        // The browser's name and type are never believed: only the content decides.
        if (MediaTypes.Detect(content) is not { Family: MediaFamily.Image })
        {
            return SendImageErrors.TypeNotAllowed;
        }

        var processed = ImageProcessor.Process(content, options.Value.MaxPixels);
        if (processed.Image is not { } image)
        {
            return processed.Failure == ImageFailure.TooManyPixels ? SendImageErrors.TooLarge : SendImageErrors.TypeNotAllowed;
        }

        var me = await team.CurrentAsync(ct) ?? throw new InvalidOperationException("Inbox endpoints require a team member.");
        var saved = new List<string>();
        try
        {
            var key = await SaveAsync(image.Full, saved, ct);
            var thumbnail = await SaveAsync(image.Thumbnail, saved, ct);

            var message = Message.FromTeamImage(tenant.TenantId, conversation.Id, me.UserId, me.DisplayName, now);
            var attachment = MessageAttachment.OutgoingImage(
                tenant.TenantId, message.Id, image.ContentType, image.Full.Length, image.Width, image.Height,
                Convert.ToHexStringLower(SHA256.HashData(content)), key, thumbnail);
            db.Messages.Add(message);
            db.Attachments.Add(attachment);
            conversation.RepliedByTeam(message);
            if (conversation.ChannelConnectionId is not null)
            {
                // Sent by the channel's background sender, which re-checks the window (ADR 0017).
                message.QueueDelivery(now);
            }

            await db.SaveChangesAsync(ct);
            await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, ct);
            return Result.Success(ConversationsHandler.ToResponse(message, [ConversationsHandler.ToResponse(attachment)]));
        }
        catch
        {
            foreach (var key in saved)
            {
                await files.DeleteAsync(key, CancellationToken.None);
            }

            throw;
        }
    }

    private async Task<string> SaveAsync(byte[] content, List<string> saved, CancellationToken ct)
    {
        using var stream = new MemoryStream(content, writable: false);
        var key = await files.SaveAsync(tenant.TenantId, stream, ct);
        saved.Add(key);
        return key;
    }
}
