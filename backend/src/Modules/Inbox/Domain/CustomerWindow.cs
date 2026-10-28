using Nihasdesk.Modules.Contacts.Contracts;

namespace Nihasdesk.Modules.Inbox.Domain;

/// <summary>
/// Meta's 24-hour rule (SEC-43): a business may answer freely only within 24 hours of the
/// customer's last message. Message tags and WhatsApp templates come later.
/// </summary>
internal static class CustomerWindow
{
    public static readonly TimeSpan Length = TimeSpan.FromHours(24);

    public static bool Applies(string channel) =>
        channel is ChannelKind.Messenger or ChannelKind.Instagram or ChannelKind.WhatsApp;

    public static bool IsOpen(string channel, DateTimeOffset? lastInboundAt, DateTimeOffset now) =>
        !Applies(channel) || (lastInboundAt is { } last && now - last < Length);
}
