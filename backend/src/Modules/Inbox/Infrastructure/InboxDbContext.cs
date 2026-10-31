using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

/// <summary>Inbox data in schema <c>inbox</c>; every table is tenant-owned (query filters + RLS).</summary>
internal sealed class InboxDbContext(DbContextOptions<InboxDbContext> options, ITenantContext tenantContext)
    : DbContext(options), ITenantDbContext
{
    public const string Schema = "inbox";

    public Guid CurrentTenantId => tenantContext.TenantId;

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<ReadMarker> ReadMarkers => Set<ReadMarker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Conversation>(conversation =>
        {
            conversation.ToTable("conversations");
            conversation.Property(c => c.Channel).HasMaxLength(Conversation.ChannelMaxLength);
            conversation.Property(c => c.Preview).HasMaxLength(Conversation.PreviewMaxLength);
            LowerCaseText(conversation.Property(c => c.Status));
            LowerCaseText(conversation.Property(c => c.Responder));
            LowerCaseText(conversation.Property(c => c.LeadStatus));
            // One conversation per contact and channel (ADR 0016).
            conversation.HasIndex(c => new { c.TenantId, c.ContactId, c.Channel }).IsUnique();
            // The inbox list: newest activity first, keyset pagination.
            conversation.HasIndex(c => new { c.TenantId, c.LastMessageAt, c.Id }).IsDescending(false, true, true);
            conversation.Ignore(c => c.AiAnswers);
            // The AI reply queue (ADR 0019).
            conversation.HasIndex(c => c.AiReplyDueAt).HasFilter("ai_reply_due_at IS NOT NULL");
        });

        modelBuilder.Entity<Message>(message =>
        {
            message.ToTable("messages");
            message.Property(m => m.Text).HasMaxLength(Message.TextMaxLength);
            message.Property(m => m.AuthorName).HasMaxLength(Message.AuthorNameMaxLength);
            message.Property(m => m.ExternalId).HasMaxLength(Message.ExternalIdMaxLength);
            message.Property(m => m.DeliveryError).HasMaxLength(Message.DeliveryErrorMaxLength);
            LowerCaseText(message.Property(m => m.Author));
            LowerCaseText(message.Property(m => m.Delivery));
            message.Property(m => m.MentionedUserIds).HasDefaultValueSql("'{}'::uuid[]");
            // Pinned notes on top of the side panel (PRD 4.4).
            message.HasIndex(m => m.ConversationId).HasFilter("pinned_at IS NOT NULL").HasDatabaseName("ix_messages_pinned");
            message.HasOne<Conversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
            message.HasIndex(m => new { m.ConversationId, m.SentAt, m.Id });
            // The background sender's queue (ADR 0017).
            message.HasIndex(m => m.NextDeliveryAt).HasFilter("delivery = 'pending'");
            // Webhook retries deliver the same channel message again.
            message.HasIndex(m => new { m.TenantId, m.ConversationId, m.ExternalId })
                .IsUnique()
                .HasFilter("external_id IS NOT NULL");
        });

        modelBuilder.Entity<ReadMarker>(marker =>
        {
            marker.ToTable("read_markers");
            marker.HasOne<Conversation>().WithMany().HasForeignKey(r => r.ConversationId).OnDelete(DeleteBehavior.Cascade);
            marker.HasIndex(r => new { r.ConversationId, r.UserId }).IsUnique();
        });

        modelBuilder.ApplyTenantQueryFilters(this);
    }

    private static void LowerCaseText<TEnum>(PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum =>
        property
            .HasConversion(value => value.ToString().ToLowerInvariant(), value => Enum.Parse<TEnum>(value, true))
            .HasMaxLength(20);
}

/// <summary>Used by <c>dotnet ef</c> with the schema-owner connection (docs/standards/database.md).</summary>
internal sealed class InboxDbContextFactory : IDesignTimeDbContextFactory<InboxDbContext>
{
    public InboxDbContext CreateDbContext(string[] args) =>
        Create(Environment.GetEnvironmentVariable("ConnectionStrings__" + PersistenceServiceCollectionExtensions.MigrationsConnectionName)
            // Placeholder so `migrations add` works offline; `database update` takes --connection.
            ?? "Host=localhost;Database=nihasdesk;Username=nihasdesk");

    public static InboxDbContext Create(string migrationsConnectionString)
    {
        var options = new DbContextOptionsBuilder<InboxDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureNpgsql(options, migrationsConnectionString, InboxDbContext.Schema);
        return new InboxDbContext(options.Options, new TenantContext());
    }
}
