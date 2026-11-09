using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Media;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

/// <summary>Limits are configurable (ADR 0031 §2); these are the Phase 1 defaults of PRD 4.4.</summary>
internal sealed class InboxMediaOptions
{
    public const string SectionName = "Inbox:Media";

    public long ImageMaxBytes { get; set; } = 10 * 1024 * 1024;

    public long AudioMaxBytes { get; set; } = 25 * 1024 * 1024;

    public long VideoMaxBytes { get; set; } = 25 * 1024 * 1024;

    public long FileMaxBytes { get; set; } = 25 * 1024 * 1024;

    public long MaxPixels { get; set; } = ImageProcessor.DefaultMaxPixels;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Downloads one attachment and checks it (ADR 0031 §2, SEC-04): the type by its content, images
/// decoded and encoded again. Nothing the channel says about a file is believed.
/// </summary>
internal sealed partial class MediaDownloader(
    InboxDbContext db,
    IEnumerable<IChannelMediaFetcher> fetchers,
    IFileStore files,
    ITenantContext tenant,
    IOptions<InboxMediaOptions> options,
    TimeProvider time)
{
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    /// <summary>A job that dies during a download leaves the row due again after this long.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private const int FileNameStemMaxLength = 100;

    public async Task DownloadAsync(Guid attachmentId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        // Taken before the download, without holding a transaction open: no other job gets it meanwhile.
        var taken = await db.Attachments
            .Where(a => a.Id == attachmentId && a.Status == AttachmentStatus.Pending && a.NextAttemptAt <= now)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.NextAttemptAt, now + Lease).SetProperty(a => a.Attempts, a => a.Attempts + 1), ct);
        if (taken == 0)
        {
            return;
        }

        var attachment = await db.Attachments.SingleAsync(a => a.Id == attachmentId, ct);
        var saved = new List<string>();
        try
        {
            await FetchAndStoreAsync(attachment, saved, now, ct);
            await db.SaveChangesAsync(ct);
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

    private async Task FetchAndStoreAsync(MessageAttachment attachment, List<string> saved, DateTimeOffset now, CancellationToken ct)
    {
        var channel = await db.Messages
            .Where(m => m.Id == attachment.MessageId)
            .Join(db.Conversations, m => m.ConversationId, c => c.Id, (_, c) => c.Channel)
            .SingleAsync(ct);
        var fetcher = fetchers.FirstOrDefault(f => f.Channel == channel);
        if (fetcher is null || attachment.SourceUrl is not { } source)
        {
            attachment.Failed(AttachmentFailure.SourceNotAllowed);
            return;
        }

        byte[] content;
        try
        {
            content = await fetcher.FetchAsync(source, LimitOf(attachment.Kind), ct);
        }
        catch (MediaFetchException ex)
        {
            switch (ex.Failure)
            {
                case MediaFetchFailure.TooLarge:
                    attachment.WasTooLarge();
                    break;
                case MediaFetchFailure.Expired:
                    attachment.Failed(AttachmentFailure.Expired);
                    break;
                case MediaFetchFailure.NotAllowed:
                    attachment.Failed(AttachmentFailure.SourceNotAllowed);
                    break;
                default:
                    if (attachment.Attempts <= Backoff.Length)
                    {
                        attachment.RetryAt(now + Backoff[attachment.Attempts - 1]);
                    }
                    else
                    {
                        attachment.Failed(AttachmentFailure.Network);
                    }

                    break;
            }

            return;
        }

        if (MediaTypes.Detect(content) is not { } detected)
        {
            attachment.Failed(AttachmentFailure.TypeNotAllowed);
            return;
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        if (detected.Family == MediaFamily.Image)
        {
            await StoreImageAsync(attachment, content, sha256, saved, ct);
            return;
        }

        var kind = KindOf(detected.Family, attachment.Kind);
        if (content.Length > LimitOf(kind))
        {
            attachment.WasTooLarge();
            return;
        }

        var contentType = detected.Family == MediaFamily.Mp4 && kind == AttachmentKind.Audio ? "audio/mp4" : detected.ContentType;
        var key = await SaveAsync(content, saved, ct);
        attachment.SavedFile(kind, contentType, content.Length, sha256, key, kind == AttachmentKind.Audio ? null : CleanFileName(source, detected.Extension));
    }

    private async Task StoreImageAsync(MessageAttachment attachment, byte[] content, string sha256, List<string> saved, CancellationToken ct)
    {
        if (content.Length > options.Value.ImageMaxBytes)
        {
            attachment.WasTooLarge();
            return;
        }

        var result = ImageProcessor.Process(content, options.Value.MaxPixels);
        switch (result)
        {
            case { Image: { } image }:
                var key = await SaveAsync(image.Full, saved, ct);
                var thumbnail = await SaveAsync(image.Thumbnail, saved, ct);
                var aiCopy = await SaveAsync(image.AiCopy, saved, ct);
                attachment.SavedImage(image.ContentType, image.Full.Length, image.Width, image.Height, sha256, key, thumbnail, aiCopy);
                break;
            case { Failure: ImageFailure.TooManyPixels }:
                attachment.WasTooLarge();
                break;
            default:
                // Looks like an image by its first bytes but isn't one we can open, e.g. HEIC where unsupported.
                attachment.Failed(AttachmentFailure.TypeNotAllowed);
                break;
        }
    }

    private async Task<string> SaveAsync(byte[] content, List<string> saved, CancellationToken ct)
    {
        using var stream = new MemoryStream(content, writable: false);
        var key = await files.SaveAsync(tenant.TenantId, stream, ct);
        saved.Add(key);
        return key;
    }

    /// <summary>Content decides, except that only the channel can say an MP4 is a voice note.</summary>
    private static AttachmentKind KindOf(MediaFamily family, AttachmentKind announced) => family switch
    {
        MediaFamily.Audio => AttachmentKind.Audio,
        MediaFamily.Mp4 => announced == AttachmentKind.Audio ? AttachmentKind.Audio : AttachmentKind.Video,
        _ => AttachmentKind.File,
    };

    private long LimitOf(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Image or AttachmentKind.Sticker => options.Value.ImageMaxBytes,
        AttachmentKind.Audio => options.Value.AudioMaxBytes,
        AttachmentKind.Video => options.Value.VideoMaxBytes,
        _ => options.Value.FileMaxBytes,
    };

    /// <summary>A name to show, from the link's last part; the extension always comes from the detected type.</summary>
    private static string CleanFileName(string source, string extension)
    {
        var stem = Uri.TryCreate(source, UriKind.Absolute, out var url)
            ? Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(url.Segments[^1]))
            : string.Empty;
        stem = UnsafeFileNameCharacters().Replace(stem, "-").Trim('-', '.');
        if (stem.Length > FileNameStemMaxLength)
        {
            stem = stem[..FileNameStemMaxLength];
        }

        return (stem.Length == 0 ? "file" : stem) + extension;
    }

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex UnsafeFileNameCharacters();
}

/// <summary>
/// Runs in the Workers host, so opening files from strangers never touches the API (ADR 0031 §2). Each
/// attachment has its own scope with its workspace set; one failure never stops the others.
/// </summary>
internal sealed partial class MediaDownloadProcessor(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<InboxMediaOptions> options,
    ILogger<MediaDownloadProcessor> logger) : BackgroundService
{
    public const int BatchSize = 5;

    /// <summary>Handles one batch; returns how many attachments were picked up. Tests call it directly.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        List<DueRow> due;
        var now = time.GetUtcNow();
        await using (var lookup = scopes.CreateAsyncScope())
        {
            // SECURITY DEFINER lookup (migration AddMessageAttachments): IDs only, no links.
            due = await lookup.ServiceProvider.GetRequiredService<InboxDbContext>().Database
                .SqlQuery<DueRow>($"SELECT tenant_id, attachment_id FROM inbox.due_media({now}, {BatchSize})")
                .ToListAsync(ct);
        }

        foreach (var item in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(item.TenantId);
                await scope.ServiceProvider.GetRequiredService<MediaDownloader>().DownloadAsync(item.AttachmentId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ex.GetType().Name;
                LogFailed(logger, item.AttachmentId, reason);
                await GiveUpAfterLastAttemptAsync(item, ct);
            }
        }

        return due.Count;
    }

    /// <summary>An unexpected error: due again when the lease ends, and after the last attempt the team is told.</summary>
    private async Task GiveUpAfterLastAttemptAsync(DueRow item, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(item.TenantId);
        var db = scope.ServiceProvider.GetRequiredService<InboxDbContext>();
        var attachment = await db.Attachments.SingleOrDefaultAsync(a => a.Id == item.AttachmentId && a.Status == AttachmentStatus.Pending, ct);
        if (attachment is not null && attachment.Attempts > MediaDownloader.Backoff.Length)
        {
            attachment.Failed(AttachmentFailure.Network);
            await db.SaveChangesAsync(ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var picked = 0;
            try
            {
                picked = await ProcessOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ex.GetType().Name;
                LogBatchFailed(logger, reason);
            }

            if (picked < BatchSize)
            {
                await Task.Delay(options.Value.PollInterval, time, stoppingToken);
            }
        }
    }

    private sealed record DueRow(Guid TenantId, Guid AttachmentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Downloading attachment {AttachmentId} failed ({Error}); tried again later")]
    private static partial void LogFailed(ILogger logger, Guid attachmentId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Media download batch failed ({Error}); retrying later")]
    private static partial void LogBatchFailed(ILogger logger, string error);
}
