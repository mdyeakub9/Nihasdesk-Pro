namespace Nihasdesk.Modules.Inbox.Contracts;

public enum MediaFetchFailure
{
    /// <summary>The link points somewhere this channel's media never is, or to a blocked address (SEC-05).</summary>
    NotAllowed,

    /// <summary>The channel no longer gives the file.</summary>
    Expired,

    TooLarge,

    /// <summary>Temporary: tried again later.</summary>
    Network,
}

public sealed class MediaFetchException(MediaFetchFailure failure) : Exception($"Media fetch failed: {failure}.")
{
    public MediaFetchFailure Failure { get; } = failure;
}

/// <summary>
/// How a channel module gets the media of its messages (ADR 0031 §1). The Inbox keeps the rows and
/// does the checks; Instagram and WhatsApp later add only their own fetcher.
/// </summary>
public interface IChannelMediaFetcher
{
    /// <summary>One of <c>Contacts.Contracts.ChannelKind</c>.</summary>
    string Channel { get; }

    /// <summary>Stops at <paramref name="maxBytes"/>. Throws <see cref="MediaFetchException"/> when it can't give the file.</summary>
    Task<byte[]> FetchAsync(string source, long maxBytes, CancellationToken cancellationToken);
}
