using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Nihasdesk.Infrastructure.Email.Outbox;

/// <summary>
/// How modules send email (ADR 0013): the message is written to the module's outbox table in the
/// same transaction as the module's data and delivered later by the Workers dispatcher. Requests
/// never wait for SMTP, so response time doesn't reveal whether an email was sent (SEC-26).
/// </summary>
public interface IEmailOutbox
{
    /// <summary>Saves the email now; inside an open transaction it commits or rolls back with it.</summary>
    Task EnqueueAsync(EmailMessage message, CancellationToken cancellationToken);
}

internal static class OutboxProtection
{
    public const string Purpose = "Nihasdesk.EmailOutbox";

    public static byte[] Protect(IDataProtector protector, EmailMessage message) =>
        protector.Protect(JsonSerializer.SerializeToUtf8Bytes(message));

    public static EmailMessage Unprotect(IDataProtector protector, byte[] data) =>
        JsonSerializer.Deserialize<EmailMessage>(protector.Unprotect(data))
        ?? throw new InvalidOperationException("Empty outbox message.");
}

internal sealed class EmailOutbox<TContext>(TContext db, IDataProtectionProvider dataProtection, TimeProvider timeProvider)
    : IEmailOutbox
    where TContext : DbContext
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(OutboxProtection.Purpose);

    public async Task EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var now = timeProvider.GetUtcNow();
        db.Set<OutboxEmail>().Add(new OutboxEmail
        {
            CreatedAt = now,
            NextAttemptAt = now,
            ProtectedMessage = OutboxProtection.Protect(_protector, message),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
