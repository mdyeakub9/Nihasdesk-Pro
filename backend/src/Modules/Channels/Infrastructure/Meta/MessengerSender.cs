using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.Modules.Channels.Infrastructure.Meta;

/// <summary>
/// Sends queued team replies through the Messenger Send API (ADR 0017). The Inbox locks each reply
/// and keeps its state; this class only knows how to reach Meta. Every reply is sent in its own
/// workspace scope, with that workspace's Page token.
/// </summary>
internal sealed partial class MessengerSender(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<ChannelsOptions> options,
    ILogger<MessengerSender> logger) : BackgroundService
{
    /// <summary>Sends one batch of due replies; returns how many were handled. Tests call it directly.</summary>
    public async Task<int> SendOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<DueDelivery> due;
        await using (var lookup = scopes.CreateAsyncScope())
        {
            due = await lookup.ServiceProvider.GetRequiredService<IOutboundMessages>().DueAsync(options.Value.BatchSize, ct);
        }

        var handled = 0;
        foreach (var delivery in due)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(delivery.TenantId);
            var outbound = scope.ServiceProvider.GetRequiredService<IOutboundMessages>();
            if (await outbound.DeliverAsync(delivery.MessageId, (message, token) => SendAsync(scope.ServiceProvider, message, token), ct))
            {
                handled++;
            }
        }

        return handled;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                handled = await SendOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBatchFailed(logger, ex.GetType().Name);
            }

            if (handled < options.Value.BatchSize)
            {
                await Task.Delay(options.Value.PollInterval, time, stoppingToken);
            }
        }
    }

    private async Task<DeliveryOutcome> SendAsync(IServiceProvider services, OutboundMessage message, CancellationToken ct)
    {
        if (message.Channel != ChannelKind.Messenger)
        {
            return DeliveryOutcome.Permanent("channel_not_supported");
        }

        var db = services.GetRequiredService<ChannelsDbContext>();
        var connection = await db.Connections.SingleOrDefaultAsync(c => c.Id == message.ConnectionId && c.DisconnectedAt == null, ct);
        if (connection is null)
        {
            return DeliveryOutcome.Permanent("channel_disconnected");
        }

        var psid = await services.GetRequiredService<IContactDirectory>().FindChannelIdAsync(message.ContactId, ChannelKind.Messenger, ct);
        if (psid is null)
        {
            return DeliveryOutcome.Permanent("recipient_unknown");
        }

        try
        {
            var token = services.GetRequiredService<PageTokens>().Unprotect(connection.ProtectedToken);
            var graph = services.GetRequiredService<MetaGraphClient>();
            var messageId = message.Image is { } image
                ? await graph.SendImageAsync(connection.ExternalAccountId, token, psid, image.Content, image.ContentType, ct)
                : await graph.SendTextAsync(connection.ExternalAccountId, token, psid, message.Text, ct);
            return DeliveryOutcome.Sent(messageId);
        }
        catch (MetaGraphException ex)
        {
            LogSendFailed(logger, message.MessageId, ex.Code, ex.Subcode);
            return ex.IsTransient ? DeliveryOutcome.Retryable(MetaGraphClient.Describe(ex)) : DeliveryOutcome.Permanent(MetaGraphClient.Describe(ex));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return DeliveryOutcome.Retryable(ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Messenger send for message {Id} failed (code {Code}, subcode {Subcode})")]
    private static partial void LogSendFailed(ILogger logger, Guid id, int code, int subcode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Messenger send batch failed ({Error}); retrying later")]
    private static partial void LogBatchFailed(ILogger logger, string error);
}
