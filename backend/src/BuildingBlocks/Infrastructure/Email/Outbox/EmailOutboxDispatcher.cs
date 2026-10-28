using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nihasdesk.Infrastructure.Email.Outbox;

public sealed class EmailOutboxOptions
{
    public const string SectionName = "Email:Outbox";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 20;
}

/// <summary>
/// Delivers a module's outbox (ADR 0013). Rows are locked with <c>FOR UPDATE SKIP LOCKED</c>, so
/// several Workers instances never send the same email twice. Sent rows are deleted; failures are
/// retried with backoff and given up after <see cref="OutboxRetry.MaxAttempts"/>, wiping the content.
/// </summary>
public sealed partial class EmailOutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    IEmailSender sender,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    IOptions<EmailOutboxOptions> options,
    ILogger<EmailOutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(OutboxProtection.Purpose);

    /// <summary>Sends one batch of due emails; returns how many were picked up. Used by tests directly.</summary>
    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        // Identifiers come from the EF model (code), validated below; values are parameters (SEC-01).
#pragma warning disable EF1003 // Only the validated, model-derived table name is concatenated.
        var due = await db.Set<OutboxEmail>()
            .FromSqlRaw(
                $"SELECT * FROM {QualifiedTable(db)} WHERE failed_at IS NULL AND next_attempt_at <= {{0}} " +
                "ORDER BY next_attempt_at LIMIT {1} FOR UPDATE SKIP LOCKED",
                now,
                options.Value.BatchSize)
            .ToListAsync(cancellationToken);
#pragma warning restore EF1003

        foreach (var email in due)
        {
            try
            {
                await sender.SendAsync(OutboxProtection.Unprotect(_protector, email.ProtectedMessage), cancellationToken);
                db.Remove(email);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                email.Attempts++;
                email.LastError = ex.GetType().Name[..Math.Min(ex.GetType().Name.Length, OutboxEmail.LastErrorMaxLength)];
                if (email.Attempts >= OutboxRetry.MaxAttempts)
                {
                    email.FailedAt = now;
                    email.ProtectedMessage = [];
                    LogGaveUp(logger, email.Id, email.Attempts, email.LastError);
                }
                else
                {
                    email.NextAttemptAt = now + OutboxRetry.Backoff[email.Attempts - 1];
                    LogRetry(logger, email.Id, email.Attempts, email.LastError);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return due.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int picked;
            try
            {
                picked = await DispatchOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database briefly unavailable etc.: keep the worker alive and try again later.
                LogBatchFailed(logger, ex.GetType().Name);
                picked = 0;
            }

            if (picked < options.Value.BatchSize)
            {
                await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
            }
        }
    }

    private static string QualifiedTable(DbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(OutboxEmail))
            ?? throw new InvalidOperationException($"{typeof(TContext).Name} does not map the email outbox.");
        var schema = entity.GetSchema() ?? "public";
        var table = entity.GetTableName()!;
        if (!Identifier().IsMatch(schema) || !Identifier().IsMatch(table))
        {
            throw new InvalidOperationException("Unexpected outbox table name.");
        }

        return $"\"{schema}\".\"{table}\"";
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex Identifier();

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox email {Id} failed (attempt {Attempt}, {Error}); will retry")]
    private static partial void LogRetry(ILogger logger, Guid id, int attempt, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox email {Id} given up after {Attempt} attempts ({Error})")]
    private static partial void LogGaveUp(ILogger logger, Guid id, int attempt, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox batch failed ({Error}); retrying later")]
    private static partial void LogBatchFailed(ILogger logger, string error);
}

public static class OutboxRetry
{
    /// <summary>Delay before attempt 2, 3, …; eight attempts span about a day.</summary>
    internal static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1),
        TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
    ];

    public static int MaxAttempts => Backoff.Length + 1;
}
