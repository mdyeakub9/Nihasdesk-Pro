using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Modules.Channels.Domain;
using Nihasdesk.Modules.Channels.Infrastructure;
using Nihasdesk.Modules.Channels.Infrastructure.Meta;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.Channels.Features.Connections;

internal sealed record ConnectionResponse(Guid Id, string Kind, string Name, string ExternalAccountId, DateTimeOffset ConnectedAt);

internal sealed record ConnectStartResponse(Uri Url);

internal static class ChannelErrors
{
    /// <summary>Also for other workspaces' IDs (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("channel.not_found", "Channel connection not found.");

    public static readonly Error NotConfigured =
        new("channel.not_configured", "The Meta app isn't configured on this server yet.", ErrorType.Failure);
}

/// <summary>
/// Connecting Facebook Pages (ADR 0017). Owner/Admin only (endpoint filter). Every query runs in
/// the current workspace; a Page connected elsewhere hits the global unique index and is skipped.
/// </summary>
internal sealed partial class ConnectionsHandler(
    ChannelsDbContext db,
    MetaGraphClient graph,
    PageTokens tokens,
    ConnectStates states,
    ITeamDirectory team,
    IAuditTrail audit,
    ITenantContext tenant,
    IOptions<MetaOptions> options,
    TimeProvider time,
    ILogger<ConnectionsHandler> logger)
{
    public async Task<IReadOnlyList<ConnectionResponse>> ListAsync(CancellationToken ct) =>
        await db.Connections
            .Where(c => c.DisconnectedAt == null)
            .OrderBy(c => c.ConnectedAt)
            .Select(c => new ConnectionResponse(c.Id, c.Kind, c.Name, c.ExternalAccountId, c.ConnectedAt))
            .ToListAsync(ct);

    public async Task<Result<ConnectStartResponse>> StartAsync(CancellationToken ct)
    {
        if (!options.Value.IsConfigured)
        {
            return ChannelErrors.NotConfigured;
        }

        var me = (await team.CurrentAsync(ct))!;
        return Result.Success(new ConnectStartResponse(graph.LoginDialogUrl(states.Create(tenant.TenantId, me.UserId))));
    }

    /// <summary>Where Facebook sends the browser back. Always answers with a dashboard URL to redirect to.</summary>
    public async Task<string> CallbackAsync(string? code, string? state, string? error, CancellationToken ct)
    {
        var me = await team.CurrentAsync(ct);
        var started = states.Read(state);
        if (me is null || started is null || started.TenantId != tenant.TenantId || started.UserId != me.UserId)
        {
            return Back("error=invalid_state");
        }

        if (error is not null || string.IsNullOrEmpty(code) || code.Length > 2000)
        {
            return Back("error=cancelled");
        }

        IReadOnlyList<MetaPage> pages;
        try
        {
            var userToken = await graph.UserTokenAsync(code, ct);
            pages = await graph.PagesAsync(userToken, ct);
        }
        catch (Exception ex) when (ex is MetaGraphException or HttpRequestException or TaskCanceledException)
        {
            LogConnectFailed(logger, ex.GetType().Name);
            return Back("error=meta_failed");
        }

        int connected = 0, elsewhere = 0;
        foreach (var page in pages.Where(p => p.Tasks.Count == 0 || p.Tasks.Contains("MESSAGING", StringComparer.Ordinal)))
        {
            if (await ConnectPageAsync(page, me.UserId, ct))
            {
                connected++;
            }
            else
            {
                elsewhere++;
            }
        }

        if (connected > 0)
        {
            await audit.RecordAsync("channel.connected", ct);
        }

        return Back($"connected={connected}&elsewhere={elsewhere}");
    }

    public async Task<Result> DisconnectAsync(Guid id, CancellationToken ct)
    {
        var connection = await db.Connections.SingleOrDefaultAsync(c => c.Id == id && c.DisconnectedAt == null, ct);
        if (connection is null)
        {
            return Result.Failure(ChannelErrors.NotFound);
        }

        try
        {
            await graph.UnsubscribeAsync(connection.ExternalAccountId, tokens.Unprotect(connection.ProtectedToken), ct);
        }
        catch (Exception ex) when (ex is MetaGraphException or HttpRequestException or TaskCanceledException)
        {
            // The token may already be revoked at Meta; disconnecting here must still work.
            var reason = ex.GetType().Name;
            LogUnsubscribeFailed(logger, connection.Id, reason);
        }

        connection.Disconnect(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("channel.disconnected", ct);
        return Result.Success();
    }

    /// <summary>Returns false when the Page is already connected to another workspace.</summary>
    private async Task<bool> ConnectPageAsync(MetaPage page, Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var protectedToken = tokens.Protect(page.AccessToken);
        var connection = await db.Connections
            .Where(c => c.Kind == ChannelKind.Messenger && c.ExternalAccountId == page.Id)
            .OrderByDescending(c => c.ConnectedAt)
            .FirstOrDefaultAsync(ct);
        if (connection is null)
        {
            db.Connections.Add(ChannelConnection.Connect(tenant.TenantId, ChannelKind.Messenger, page.Id, page.Name, protectedToken, userId, now));
        }
        else
        {
            connection.Refresh(page.Name, protectedToken, userId, now);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }

        try
        {
            await graph.SubscribeAsync(page.Id, page.AccessToken, ct);
        }
        catch (Exception ex) when (ex is MetaGraphException or HttpRequestException or TaskCanceledException)
        {
            // Connected, but Meta won't send messages yet; connecting again retries the subscription.
            LogSubscribeFailed(logger, ex.GetType().Name);
        }

        return true;
    }

    private string Back(string query) => $"{options.Value.DashboardUrl.TrimEnd('/')}/settings/channels?{query}";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connecting Meta pages failed ({Error})")]
    private static partial void LogConnectFailed(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscribing a Page to webhooks failed ({Error})")]
    private static partial void LogSubscribeFailed(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unsubscribing connection {Id} failed ({Error}); disconnecting anyway")]
    private static partial void LogUnsubscribeFailed(ILogger logger, Guid id, string error);
}
