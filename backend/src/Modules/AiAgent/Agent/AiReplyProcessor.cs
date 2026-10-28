using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>
/// Answers due conversations in the background (ADR 0019). In the API like the channel processors
/// (ADR 0017), so replies reach dashboards over SignalR. Each conversation gets its own scope with
/// its workspace set; one failure never stops the others.
/// </summary>
internal sealed partial class AiReplyProcessor(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<AiOptions> options,
    ILogger<AiReplyProcessor> logger) : BackgroundService
{
    public const int BatchSize = 10;

    /// <summary>Answers one batch; returns how many conversations were picked up. Tests call it directly.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<DueAiReply> due;
        await using (var lookup = scopes.CreateAsyncScope())
        {
            // No API key: the AI stays off and the team answers as before (ADR 0019).
            if (!lookup.ServiceProvider.GetRequiredService<ILlmProvider>().IsConfigured)
            {
                return 0;
            }

            due = await lookup.ServiceProvider.GetRequiredService<IAiConversations>().DueAsync(BatchSize, ct);
        }

        foreach (var item in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(item.TenantId);
                try
                {
                    await scope.ServiceProvider.GetRequiredService<ReplyAgent>().RunAsync(item.ConversationId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var reason = ex.GetType().Name;
                    LogFailed(logger, item.ConversationId, reason);
                    // Never leave a customer without anyone answering: the team takes over.
                    await HandOffAfterErrorAsync(item, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ex.GetType().Name;
                LogFailed(logger, item.ConversationId, reason);
            }
        }

        return due.Count;
    }

    private async Task HandOffAfterErrorAsync(DueAiReply item, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(item.TenantId);
        await scope.ServiceProvider.GetRequiredService<IAiConversations>()
            .HandOffAsync(item.ConversationId, "The AI ran into a problem with this conversation. Please answer this customer.", ct);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI reply for conversation {ConversationId} failed ({Error})")]
    private static partial void LogFailed(ILogger logger, Guid conversationId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI reply batch failed ({Error}); retrying later")]
    private static partial void LogBatchFailed(ILogger logger, string error);
}
