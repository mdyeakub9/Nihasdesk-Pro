using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Identity.Accounts;

namespace Nihasdesk.Modules.Identity.Infrastructure.Sessions;

/// <summary>
/// Server-side session store for the auth cookie (ADR 0010). Enforces the idle and absolute
/// lifetimes on every request; deleting a row signs that session out immediately.
/// </summary>
internal sealed class DatabaseTicketStore(
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider) : ITicketStore
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Nihasdesk.Identity.Sessions");

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, httpContext: null!, CancellationToken.None);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var now = timeProvider.GetUtcNow();
        var remember = ticket.Properties.IsPersistent;
        var userAgent = httpContext?.Request.Headers.UserAgent.ToString();

        var session = new UserSession
        {
            UserId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
            TenantId = Guid.Parse(ticket.Principal.FindFirstValue(TenantClaims.TenantId)!),
            Remember = remember,
            UserAgent = string.IsNullOrEmpty(userAgent) ? null : userAgent[..Math.Min(userAgent.Length, UserSession.UserAgentMaxLength)],
            CreatedAt = now,
            LastSeenAt = now,
            IdleExpiresAt = now + SessionLifetime.Idle(remember),
            AbsoluteExpiresAt = now + SessionLifetime.Absolute(remember),
        };
        // The stored ticket carries its own session id (for re-auth and "this device").
        if (ticket.Principal.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim(SessionClaims.SessionId, session.Id.ToString()));
        }

        session.Ticket = Protect(ticket);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return session.Id.ToString();
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        var protectedTicket = Protect(ticket);
        await db.Sessions.Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Ticket, protectedTicket), cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (now >= session.IdleExpiresAt || now >= session.AbsoluteExpiresAt)
        {
            db.Sessions.Remove(session);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (now - session.LastSeenAt >= SessionLifetime.TouchInterval)
        {
            session.LastSeenAt = now;
            var idleExpiry = now + SessionLifetime.Idle(session.Remember);
            session.IdleExpiresAt = idleExpiry < session.AbsoluteExpiresAt ? idleExpiry : session.AbsoluteExpiresAt;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Unprotect(session.Ticket);
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        await db.Sessions.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    private byte[] Protect(AuthenticationTicket ticket) => _protector.Protect(TicketSerializer.Default.Serialize(ticket));

    private AuthenticationTicket? Unprotect(byte[] data)
    {
        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(data));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Key ring lost or rotated out: treat as signed out.
            return null;
        }
    }
}
