using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Channels.Features.Webhooks;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.Modules.Channels.Infrastructure.Meta;

/// <summary>
/// Turns stored Meta webhooks into inbox messages (ADR 0017). Each event is locked with
/// <c>FOR UPDATE SKIP LOCKED</c>; the workspace comes only from the connected Page ID, looked up in
/// the database, never from anything else in the payload (CLAUDE.md rule 1).
/// </summary>
internal sealed partial class MessengerWebhookProcessor(
    IServiceScopeFactory scopes,
    IDataProtectionProvider dataProtection,
    IMemoryCache profileNames,
    TimeProvider time,
    IOptions<ChannelsOptions> options,
    ILogger<MessengerWebhookProcessor> logger) : BackgroundService
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)];

    private readonly IDataProtector _payloads = WebhookPayload.Protector(dataProtection);

    /// <summary>Handles one batch; returns how many events were picked up. Tests call it directly.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChannelsDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = time.GetUtcNow();
        var due = await db.WebhookEvents
            .FromSql($"SELECT * FROM channels.webhook_events WHERE processed_at IS NULL AND next_attempt_at <= {now} ORDER BY received_at LIMIT {options.Value.BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);

        foreach (var webhook in due)
        {
            try
            {
                await HandleAsync(db, _payloads.Unprotect(webhook.ProtectedPayload), ct);
                webhook.ProcessedAt = now;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                webhook.Attempts++;
                webhook.LastError = ex.GetType().Name[..Math.Min(ex.GetType().Name.Length, Webhooks.WebhookEvent.LastErrorMaxLength)];
                if (webhook.Attempts > Backoff.Length)
                {
                    webhook.ProcessedAt = now;
                    LogGaveUp(logger, webhook.Id, webhook.LastError);
                }
                else
                {
                    webhook.NextAttemptAt = now + Backoff[webhook.Attempts - 1];
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Raw payloads are personal data: keep them only as long as they help debugging.
        var cutoff = now - Retention;
        await db.WebhookEvents.Where(w => w.ProcessedAt != null && w.ProcessedAt < cutoff).ExecuteDeleteAsync(ct);
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
                LogBatchFailed(logger, ex.GetType().Name);
            }

            if (picked < options.Value.BatchSize)
            {
                await Task.Delay(options.Value.PollInterval, time, stoppingToken);
            }
        }
    }

    private async Task HandleAsync(ChannelsDbContext db, byte[] payload, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (Text(root, "object") != "page" || !root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (Text(entry, "id") is not { } pageId
                || !entry.TryGetProperty("messaging", out var events)
                || events.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var connection = (await db.Database
                .SqlQuery<ConnectionRow>($"SELECT id, tenant_id FROM channels.find_connection({ChannelKind.Messenger}, {pageId})")
                .ToListAsync(ct)).SingleOrDefault();
            if (connection is null)
            {
                LogUnknownPage(logger);
                continue;
            }

            foreach (var messaging in events.EnumerateArray())
            {
                if (Parse(messaging) is { } inbound)
                {
                    await ReceiveAsync(connection, pageId, inbound, ct);
                }
            }
        }
    }

    private async Task ReceiveAsync(ConnectionRow connection, string pageId, ParsedMessage inbound, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(connection.TenantId);

        var name = await ProfileNameAsync(scope.ServiceProvider, connection.Id, pageId, inbound.SenderId, ct);
        var ingest = scope.ServiceProvider.GetRequiredService<IInboxIngest>();
        await ingest.ReceiveAsync(
            new InboundMessage(ChannelKind.Messenger, inbound.SenderId, name, inbound.Text, inbound.MessageId, inbound.SentAt, connection.Id),
            ct);
    }

    /// <summary>Best effort and cached: a missing name never blocks a message.</summary>
    private async Task<string?> ProfileNameAsync(IServiceProvider services, Guid connectionId, string pageId, string psid, CancellationToken ct)
    {
        var key = $"meta-profile:{pageId}:{psid}";
        if (profileNames.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        string? name = null;
        try
        {
            var db = services.GetRequiredService<ChannelsDbContext>();
            var protectedToken = await db.Connections.Where(c => c.Id == connectionId).Select(c => c.ProtectedToken).SingleAsync(ct);
            name = await services.GetRequiredService<MetaGraphClient>()
                .ProfileNameAsync(psid, services.GetRequiredService<PageTokens>().Unprotect(protectedToken), ct);
        }
        catch (Exception ex) when (ex is MetaGraphException or HttpRequestException or TaskCanceledException)
        {
            var reason = ex.GetType().Name;
            LogProfileFailed(logger, reason);
        }

        profileNames.Set(key, name, TimeSpan.FromHours(12));
        return name;
    }

    /// <summary>Customer text messages only; echoes of the Page's own messages, reads and reactions are skipped.</summary>
    private static ParsedMessage? Parse(JsonElement messaging)
    {
        if (!messaging.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || (message.TryGetProperty("is_echo", out var echo) && echo.ValueKind == JsonValueKind.True)
            || !messaging.TryGetProperty("sender", out var sender)
            || Text(sender, "id") is not { } senderId
            || !senderId.All(char.IsAsciiDigit)
            || Text(message, "mid") is not { } messageId)
        {
            return null;
        }

        var text = Text(message, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            // Media storage comes later (ADR 0017).
            text = message.TryGetProperty("attachments", out _) ? "[attachment]" : null;
        }

        if (text is null)
        {
            return null;
        }

        var sentAt = messaging.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : DateTimeOffset.UtcNow;
        const int max = 4000;
        return new ParsedMessage(senderId, messageId.Length <= 200 ? messageId : messageId[..200], text.Length <= max ? text : text[..max], sentAt);
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record ParsedMessage(string SenderId, string MessageId, string Text, DateTimeOffset SentAt);

    private sealed record ConnectionRow(Guid Id, Guid TenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook for a Page that isn't connected; ignored")]
    private static partial void LogUnknownPage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Messenger profile lookup failed ({Error})")]
    private static partial void LogProfileFailed(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook event {Id} given up ({Error})")]
    private static partial void LogGaveUp(ILogger logger, Guid id, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook batch failed ({Error}); retrying later")]
    private static partial void LogBatchFailed(ILogger logger, string error);
}
