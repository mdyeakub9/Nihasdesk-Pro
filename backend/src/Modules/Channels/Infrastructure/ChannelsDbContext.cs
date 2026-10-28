using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Channels.Domain;
using Nihasdesk.Modules.Channels.Infrastructure.Webhooks;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Channels.Infrastructure;

/// <summary>
/// Channels data in schema <c>channels</c>. Connections are tenant-owned (query filters + RLS).
/// Webhook events arrive before their workspace is known, so they are not tenant-owned: their
/// payload is encrypted and only the webhook processor reads them (ADR 0017).
/// </summary>
internal sealed class ChannelsDbContext(DbContextOptions<ChannelsDbContext> options, ITenantContext tenantContext)
    : DbContext(options), ITenantDbContext
{
    public const string Schema = "channels";

    public Guid CurrentTenantId => tenantContext.TenantId;

    public DbSet<ChannelConnection> Connections => Set<ChannelConnection>();

    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ChannelConnection>(connection =>
        {
            connection.ToTable("channel_connections");
            connection.Property(c => c.Kind).HasMaxLength(ChannelConnection.KindMaxLength);
            connection.Property(c => c.ExternalAccountId).HasMaxLength(ChannelConnection.ExternalAccountIdMaxLength);
            connection.Property(c => c.Name).HasMaxLength(ChannelConnection.NameMaxLength);
            connection.Ignore(c => c.IsActive);
            // One workspace per Page, across all workspaces (ADR 0017).
            connection.HasIndex(c => new { c.Kind, c.ExternalAccountId })
                .IsUnique()
                .HasFilter("disconnected_at IS NULL")
                .HasDatabaseName("ux_channel_connections_active_account");
            connection.HasIndex(c => new { c.TenantId, c.Kind, c.ExternalAccountId });
        });

        modelBuilder.Entity<WebhookEvent>(webhook =>
        {
            webhook.ToTable("webhook_events");
            webhook.Property(w => w.Provider).HasMaxLength(WebhookEvent.ProviderMaxLength);
            webhook.Property(w => w.LastError).HasMaxLength(WebhookEvent.LastErrorMaxLength);
            webhook.HasIndex(w => w.NextAttemptAt).HasFilter("processed_at IS NULL");
            webhook.HasIndex(w => w.ProcessedAt);
        });

        modelBuilder.ApplyTenantQueryFilters(this);
    }
}

/// <summary>Used by <c>dotnet ef</c> with the schema-owner connection (docs/standards/database.md).</summary>
internal sealed class ChannelsDbContextFactory : IDesignTimeDbContextFactory<ChannelsDbContext>
{
    public ChannelsDbContext CreateDbContext(string[] args) =>
        Create(Environment.GetEnvironmentVariable("ConnectionStrings__" + PersistenceServiceCollectionExtensions.MigrationsConnectionName)
            // Placeholder so `migrations add` works offline; `database update` takes --connection.
            ?? "Host=localhost;Database=nihasdesk;Username=nihasdesk");

    public static ChannelsDbContext Create(string migrationsConnectionString)
    {
        var options = new DbContextOptionsBuilder<ChannelsDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureNpgsql(options, migrationsConnectionString, ChannelsDbContext.Schema);
        return new ChannelsDbContext(options.Options, new TenantContext());
    }
}
