using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.Modules.AiAgent.Infrastructure;

/// <summary>
/// Removes run traces older than <see cref="AiOptions.TraceRetentionDays"/> (SEC-40), once a day,
/// for all workspaces through the narrow <c>ai.purge_old_traces</c> function (migration
/// AddRunTraceAndFeedback). The run rows stay: limits and metering count them.
/// </summary>
internal sealed partial class TraceRetention(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<AiOptions> options,
    ILogger<TraceRetention> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>Returns how many traces were removed. Tests call it directly.</summary>
    public async Task<int> PurgeOnceAsync(CancellationToken ct)
    {
        var before = time.GetUtcNow().AddDays(-options.Value.TraceRetentionDays);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var traces = await db.Database
            .SqlQuery<int>($"SELECT ai.purge_old_traces({before}) AS \"Value\"")
            .SingleAsync(ct);

        // Order drafts (and any delivery details in them) older than 7 days (ADR 0032, SEC-44).
        var draftsBefore = time.GetUtcNow() - OrderDraft.Lifetime;
        await db.Database.SqlQuery<int>($"SELECT ai.purge_old_drafts({draftsBefore}) AS \"Value\"").SingleAsync(ct);
        return traces;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var purged = await PurgeOnceAsync(stoppingToken);
                LogPurged(logger, purged);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ex.GetType().Name;
                LogFailed(logger, reason);
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} AI run traces past retention")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Removing old AI run traces failed ({Error}); retrying tomorrow")]
    private static partial void LogFailed(ILogger logger, string error);
}
