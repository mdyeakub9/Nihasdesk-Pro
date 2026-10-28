using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Nihasdesk.Modules.Channels.Infrastructure.Meta;

/// <summary>Page tokens encrypted at rest with Data Protection; the key ring lives outside the table (SEC-28).</summary>
internal sealed class PageTokens(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Nihasdesk.Channels.PageToken.v1");

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);
}

/// <summary>What the OAuth <c>state</c> carries: who started the connection, for which workspace.</summary>
internal sealed record ConnectState(Guid TenantId, Guid UserId, string Nonce);

/// <summary>
/// The OAuth <c>state</c>: encrypted, valid 10 minutes and bound to workspace and user, so a
/// callback can't be replayed or started by someone else (ADR 0017).
/// </summary>
internal sealed class ConnectStates(IDataProtectionProvider provider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("Nihasdesk.Channels.MetaConnectState.v1").ToTimeLimitedDataProtector();

    public string Create(Guid tenantId, Guid userId) =>
        _protector.Protect(
            JsonSerializer.Serialize(new ConnectState(tenantId, userId, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))),
            Lifetime);

    public ConnectState? Read(string? state)
    {
        if (string.IsNullOrEmpty(state) || state.Length > 2000)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ConnectState>(_protector.Unprotect(state));
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
