using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Inbox.Domain;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

/// <summary>
/// Removes media files older than <see cref="InboxMediaOptions.RetentionDays"/> (SEC-44, ADR 0031 §8),
/// once a day in Workers. Due rows of all workspaces come from the narrow <c>inbox.expired_media</c>
/// lookup (IDs only); each one is removed in its own workspace scope. The files go first, so a failure
/// leaves the row due again rather than a file nobody knows about.
/// </summary>
internal sealed partial class MediaRetention(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<InboxMediaOptions> options,
    ILogger<MediaRetention> logger) : BackgroundService
{
    public const int BatchSize = 100;

    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>Removes everything due now, in batches; returns how many attachments were removed. Tests call it directly.</summary>
    public async Task<int> PurgeOnceAsync(CancellationToken ct)
    {
        var before = time.GetUtcNow().AddDays(-options.Value.RetentionDays);
        var removed = 0;
        while (true)
        {
            List<DueRow> due;
            await using (var lookup = scopes.CreateAsyncScope())
            {
                due = await lookup.ServiceProvider.GetRequiredService<InboxDbContext>().Database
                    .SqlQuery<DueRow>($"SELECT tenant_id, attachment_id FROM inbox.expired_media({before}, {BatchSize})")
                    .ToListAsync(ct);
            }

            foreach (var item in due)
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(item.TenantId);
                var db = scope.ServiceProvider.GetRequiredService<InboxDbContext>();
                var files = scope.ServiceProvider.GetRequiredService<IFileStore>();
                var attachment = await db.Attachments.SingleAsync(a => a.Id == item.AttachmentId, ct);
                foreach (var key in attachment.StoredKeys())
                {
                    await files.DeleteAsync(key, ct);
                }

                attachment.Removed();
                await db.SaveChangesAsync(ct);
                removed++;
            }

            if (due.Count < BatchSize)
            {
                return removed;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = await PurgeOnceAsync(stoppingToken);
                LogRemoved(logger, removed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ex.GetType().Name;
                LogFailed(logger, reason);
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }

    private sealed record DueRow(Guid TenantId, Guid AttachmentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} media files past retention")]
    private static partial void LogRemoved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Removing old media failed ({Error}); retrying tomorrow")]
    private static partial void LogFailed(ILogger logger, string error);
}
