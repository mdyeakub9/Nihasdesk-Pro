using Microsoft.EntityFrameworkCore;

namespace Nihasdesk.Infrastructure.Email.Outbox;

/// <summary>
/// An email waiting to be sent (ADR 0013). The whole message — recipient included — is stored
/// encrypted with Data Protection, because bodies carry sign-in and reset tokens. The row is
/// deleted once sent; after the last failed attempt only metadata is kept.
/// </summary>
public sealed class OutboxEmail
{
    public const string TableName = "email_outbox";

    public const int LastErrorMaxLength = 200;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; init; }

    public byte[] ProtectedMessage { get; set; } = [];

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>Set when delivery was given up; the message content is wiped at the same time.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>Exception type of the last failure; never message text, which may contain addresses.</summary>
    public string? LastError { get; set; }
}

public static class OutboxModelBuilderExtensions
{
    /// <summary>Adds the outbox table to a module's DbContext, in the module's own schema.</summary>
    public static ModelBuilder MapEmailOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxEmail>(outbox =>
        {
            outbox.ToTable(OutboxEmail.TableName);
            outbox.Property(o => o.LastError).HasMaxLength(OutboxEmail.LastErrorMaxLength);
            outbox.HasIndex(o => o.NextAttemptAt).HasFilter("failed_at IS NULL");
        });
        return modelBuilder;
    }
}
