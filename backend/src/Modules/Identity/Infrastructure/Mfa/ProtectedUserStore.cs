using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Identity.Accounts;

namespace Nihasdesk.Modules.Identity.Infrastructure.Mfa;

/// <summary>
/// The Identity user store with two changes (ADR 0015, SEC-09): the TOTP authenticator secret is
/// stored encrypted with Data Protection, and recovery codes live as SHA-256 hashes in their own
/// table instead of the plain-text token Identity uses by default.
/// </summary>
internal sealed class ProtectedUserStore(
    IdentityModuleDbContext context,
    IDataProtectionProvider dataProtection,
    TimeProvider time,
    IdentityErrorDescriber? describer = null)
    : UserOnlyStore<User, IdentityModuleDbContext, Guid>(context, describer)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Nihasdesk.Identity.AuthenticatorKey");

    public override async Task SetAuthenticatorKeyAsync(User user, string key, CancellationToken cancellationToken)
    {
        await base.SetAuthenticatorKeyAsync(user, _protector.Protect(key), cancellationToken);
    }

    public override async Task<string?> GetAuthenticatorKeyAsync(User user, CancellationToken cancellationToken)
    {
        var stored = await base.GetAuthenticatorKeyAsync(user, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(stored);
        }
        catch (CryptographicException)
        {
            // Key ring lost: the user must set MFA up again (recovery codes still work).
            return null;
        }
    }

    public override async Task ReplaceCodesAsync(User user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(recoveryCodes);
        var codes = Context.Set<RecoveryCode>();
        codes.RemoveRange(await codes.Where(c => c.UserId == user.Id).ToListAsync(cancellationToken));
        var now = time.GetUtcNow();
        codes.AddRange(recoveryCodes.Select(code => new RecoveryCode { UserId = user.Id, CodeHash = Hash(code), CreatedAt = now }));
        // Saved by UserManager's following UpdateAsync, together with the user.
    }

    public override async Task<bool> RedeemCodeAsync(User user, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var hash = Hash(code);
        var match = await Context.Set<RecoveryCode>()
            .Where(c => c.UserId == user.Id && c.UsedAt == null)
            .ToListAsync(cancellationToken);
        var found = match.FirstOrDefault(c => CryptographicOperations.FixedTimeEquals(c.CodeHash, hash));
        if (found is null)
        {
            return false;
        }

        found.UsedAt = time.GetUtcNow();
        return true;
    }

    public override Task<int> CountCodesAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Context.Set<RecoveryCode>().CountAsync(c => c.UserId == user.Id && c.UsedAt == null, cancellationToken);
    }

    /// <summary>Codes are compared case-insensitively and without dashes or spaces.</summary>
    private static byte[] Hash(string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(code.Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant()));
}
