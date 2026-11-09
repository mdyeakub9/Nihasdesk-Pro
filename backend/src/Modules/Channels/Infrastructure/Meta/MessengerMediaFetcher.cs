using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.Modules.Channels.Infrastructure.Meta;

/// <summary>
/// Messenger media comes as a link in the webhook (ADR 0031 §2). Only Meta's CDN hosts are fetched,
/// redirects included, on top of all the checks of <see cref="SafeHttpClient"/> (SEC-05). Page tokens
/// are never sent: these links work without one.
/// </summary>
internal sealed class MessengerMediaFetcher(SafeHttpClient http) : IChannelMediaFetcher
{
    /// <summary>A big video on a slow link takes longer than a web page.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private static readonly string[] MetaCdnDomains = ["fbcdn.net", "fbsbx.com"];

    public string Channel => ChannelKind.Messenger;

    /// <summary>The domain itself or a real subdomain of it; <c>evilfbcdn.net</c> doesn't count.</summary>
    public static bool IsMetaCdn(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.Scheme == Uri.UriSchemeHttps
            && MetaCdnDomains.Any(d => url.IdnHost.Equals(d, StringComparison.OrdinalIgnoreCase)
                || url.IdnHost.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<byte[]> FetchAsync(string source, long maxBytes, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var url))
        {
            throw new MediaFetchException(MediaFetchFailure.NotAllowed);
        }

        try
        {
            return (await http.GetAsync(url, maxBytes, IsMetaCdn, Timeout, cancellationToken)).Body;
        }
        catch (SafeHttpException ex)
        {
            throw new MediaFetchException(ex.Failure switch
            {
                SafeHttpFailure.TooLarge => MediaFetchFailure.TooLarge,
                SafeHttpFailure.Unreachable or SafeHttpFailure.Timeout => MediaFetchFailure.Network,
                // Rate limits and outages pass; anything else from Meta means the link is no longer good.
                SafeHttpFailure.BadStatus => ex.Status is 429 or >= 500 ? MediaFetchFailure.Network : MediaFetchFailure.Expired,
                _ => MediaFetchFailure.NotAllowed,
            });
        }
    }
}
