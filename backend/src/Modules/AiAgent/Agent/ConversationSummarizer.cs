using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>
/// Adds a conversation's older messages to its running summary, with the cheap model (ADR 0026 §4).
/// It reads only what customers saw, never team notes (SEC-37), and meters the call like a reply.
/// </summary>
internal sealed partial class ConversationSummarizer(
    ILlmProvider llm,
    IAiMessageReader messages,
    AiDbContext db,
    ITenantContext tenant,
    IOptions<AiOptions> options,
    TimeProvider time,
    ILogger<ConversationSummarizer> logger)
{
    public const int MaxAttempts = 3;

    public const int BatchSize = 200;

    private static readonly TimeSpan Lock = TimeSpan.FromMinutes(5);

    public async Task RunAsync(Guid conversationId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var memory = await db.Memory.SingleOrDefaultAsync(m => m.ConversationId == conversationId, ct);
        if (memory?.SummaryDueAt is null || memory.SummaryLockUntil > now)
        {
            return;
        }

        memory.Claim(now.Add(Lock));
        await db.SaveChangesAsync(ct);

        var rows = await messages.AfterAsync(conversationId, memory.CoveredUpToAt, memory.CoveredUpToMessageId, BatchSize, ct);
        var older = ContextBuilder.Older(rows, ContextBuilder.SummaryStartBudget(options.Value));
        if (older.Count == 0)
        {
            memory.Done();
            await db.SaveChangesAsync(ct);
            return;
        }

        var watch = Stopwatch.StartNew();
        LlmResponse? response = null;
        var failure = "empty_summary";
        try
        {
            response = await llm.CompleteAsync(
                new LlmRequest(
                    options.Value.SummaryModel,
                    [LlmMessage.System(Prompt.Summarizer), LlmMessage.User(Prompt.SummaryInput(memory.Summary, older))],
                    [],
                    options.Value.SummaryMaxOutputTokens),
                ct);
        }
        catch (LlmException ex)
        {
            failure = ex.Reason;
        }

        var summary = Clean(response?.Text);
        if (summary is null)
        {
            LogFailed(logger, conversationId, failure);
            if (memory.SummaryAttempts >= MaxAttempts)
            {
                memory.Done();
            }
        }
        else
        {
            memory.Cover(summary, older[^1].SentAt, older[^1].Id);
            // A full batch may have more behind it; otherwise the newest messages are still in the context.
            if (rows.Count < BatchSize)
            {
                memory.Done();
            }
        }

        db.Runs.Add(AiRun.Finish(
            tenant.TenantId, conversationId, options.Value.SummaryModel, response?.Usage.InputTokens ?? 0, response?.Usage.OutputTokens ?? 0, [],
            summary is null ? RunOutcome.Failed : RunOutcome.Summarized, summary is null ? failure : null, watch.Elapsed,
            instructionVersion: Prompt.SummaryVersion));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The word limit and the removal of contact details are checked by code, not left to the model (rule 7).</summary>
    internal static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var scrubbed = Phone().Replace(Email().Replace(text, "[removed]"), "[removed]");
        var words = scrubbed.Split((char[]?)null, ConversationMemory.SummaryMaxWords + 1, StringSplitOptions.RemoveEmptyEntries);
        var limited = new StringBuilder(string.Join(' ', words.Take(ConversationMemory.SummaryMaxWords)));
        return limited.Length <= ConversationMemory.SummaryMaxLength ? limited.ToString() : limited.ToString(0, ConversationMemory.SummaryMaxLength);
    }

    [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+")]
    private static partial Regex Email();

    // Seven or more digits, with the usual separators. A short order number like #1003 stays.
    [GeneratedRegex(@"\+?\d(?:[\s().-]*\d){6,}")]
    private static partial Regex Phone();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Summary for conversation {ConversationId} failed ({Reason})")]
    private static partial void LogFailed(ILogger logger, Guid conversationId, string reason);
}

/// <summary>Runs due summaries in the Workers host. Each has its own scope with its workspace set; one failure never stops the others.</summary>
internal sealed partial class ConversationSummaryProcessor(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<AiOptions> options,
    ILogger<ConversationSummaryProcessor> logger) : BackgroundService
{
    public const int BatchSize = 10;

    /// <summary>Runs one batch; returns how many conversations were picked up. Tests call it directly.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        List<DueRow> due;
        var now = time.GetUtcNow();
        await using (var lookup = scopes.CreateAsyncScope())
        {
            if (!lookup.ServiceProvider.GetRequiredService<ILlmProvider>().IsConfigured)
            {
                return 0;
            }

            // SECURITY DEFINER lookup (migration AddConversationMemory): IDs only.
            due = await lookup.ServiceProvider.GetRequiredService<AiDbContext>().Database
                .SqlQuery<DueRow>($"SELECT tenant_id, conversation_id FROM ai.due_memory_summaries({now}, {BatchSize})")
                .ToListAsync(ct);
        }

        foreach (var item in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(item.TenantId);
                await scope.ServiceProvider.GetRequiredService<ConversationSummarizer>().RunAsync(item.ConversationId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reason = ex.GetType().Name;
                LogFailed(logger, item.ConversationId, reason);
            }
        }

        return due.Count;
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

    private sealed record DueRow(Guid TenantId, Guid ConversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Summary job for conversation {ConversationId} failed ({Error}); tried again later")]
    private static partial void LogFailed(ILogger logger, Guid conversationId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Summary batch failed ({Error}); retrying later")]
    private static partial void LogBatchFailed(ILogger logger, string error);
}
