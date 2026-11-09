using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Features.Media;

internal static class MediaErrors
{
    /// <summary>Also for other workspaces' media and media that isn't saved (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("media.not_found", "Media not found.");

    public static readonly Error UnknownVariant = Error.Validation("media.unknown_variant", "Variant must be thumbnail, or left out.");
}

/// <summary>
/// <c>/api/v1/media/{id}</c> (ADR 0031 §1): customer media for signed-in team members only, read through
/// the tenant-scoped context. The AI's copy is never served. Only images and audio open in the browser.
/// </summary>
internal static class MediaEndpoints
{
    public const string Thumbnail = "thumbnail";

    public static string UrlOf(Guid id) => $"/api/v1/media/{id}";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/v1/media")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>()
            .MapGet("/{id:guid}", GetAsync);
    }

    private static async Task<IResult> GetAsync(Guid id, string? variant, InboxDbContext db, IFileStore files, HttpResponse response, CancellationToken ct)
    {
        if (variant is not (null or Thumbnail))
        {
            return MediaErrors.UnknownVariant.ToProblem();
        }

        var attachment = await db.Attachments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id && a.Status == AttachmentStatus.Saved, ct);
        var key = variant == Thumbnail ? attachment?.ThumbnailKey : attachment?.StorageKey;
        if (attachment?.ContentType is not { } contentType || key is null)
        {
            return MediaErrors.NotFound.ToProblem();
        }

        Stream content;
        try
        {
            content = await files.OpenReadAsync(key, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return MediaErrors.NotFound.ToProblem();
        }

        var headers = response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        // Personal data: only this browser may keep it, never a shared cache.
        headers.CacheControl = "private, max-age=3600";

        if (attachment.Kind is AttachmentKind.Image or AttachmentKind.Sticker or AttachmentKind.Audio)
        {
            headers.ContentDisposition = "inline";
            return Results.File(content, contentType, enableRangeProcessing: true);
        }

        return Results.File(content, contentType, attachment.FileName ?? "file", enableRangeProcessing: true);
    }
}
