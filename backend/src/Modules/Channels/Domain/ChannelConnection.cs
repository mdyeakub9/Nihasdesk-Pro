using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Channels.Domain;

/// <summary>
/// A channel account connected to a workspace, e.g. one Facebook Page (ADR 0017). The access
/// token is stored already encrypted by the infrastructure (SEC-28) and wiped on disconnect.
/// </summary>
internal sealed class ChannelConnection : TenantEntity
{
    public const int KindMaxLength = 20;
    public const int ExternalAccountIdMaxLength = 100;
    public const int NameMaxLength = 200;

    private ChannelConnection(Guid tenantId, string kind, string externalAccountId, string name, string protectedToken)
        : base(tenantId)
    {
        Kind = kind;
        ExternalAccountId = externalAccountId;
        Name = name;
        ProtectedToken = protectedToken;
    }

    /// <summary>A <c>Contacts.Contracts.ChannelKind</c>, e.g. <c>messenger</c>.</summary>
    public string Kind { get; private set; }

    /// <summary>The account's ID at the provider, e.g. the Page ID.</summary>
    public string ExternalAccountId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Data-Protection-encrypted access token; empty once disconnected.</summary>
    public string ProtectedToken { get; private set; }

    public Guid ConnectedByUserId { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    public DateTimeOffset? DisconnectedAt { get; private set; }

    public bool IsActive => DisconnectedAt is null;

    public static ChannelConnection Connect(
        Guid tenantId, string kind, string externalAccountId, string name, string protectedToken, Guid userId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(externalAccountId) || externalAccountId.Length > ExternalAccountIdMaxLength)
        {
            throw new ArgumentException("External account ID is required and at most 100 characters.", nameof(externalAccountId));
        }

        var connection = new ChannelConnection(tenantId, kind, externalAccountId, Shorten(name), protectedToken);
        connection.Refresh(name, protectedToken, userId, now);
        return connection;
    }

    /// <summary>Connected again (or the token renewed): active with the new token.</summary>
    public void Refresh(string name, string protectedToken, Guid userId, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(protectedToken))
        {
            throw new ArgumentException("A token is required.", nameof(protectedToken));
        }

        Name = Shorten(name);
        ProtectedToken = protectedToken;
        ConnectedByUserId = userId;
        ConnectedAt = now;
        DisconnectedAt = null;
    }

    public void Disconnect(DateTimeOffset now)
    {
        ProtectedToken = string.Empty;
        DisconnectedAt = now;
    }

    private static string Shorten(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Page" : name.Trim();
        return trimmed.Length <= NameMaxLength ? trimmed : trimmed[..NameMaxLength];
    }
}
