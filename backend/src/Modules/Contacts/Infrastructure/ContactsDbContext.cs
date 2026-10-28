using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Contacts.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Contacts.Infrastructure;

/// <summary>Contacts module data in schema <c>contacts</c>; every table is tenant-owned (query filters + RLS).</summary>
internal sealed class ContactsDbContext(DbContextOptions<ContactsDbContext> options, ITenantContext tenantContext)
    : DbContext(options), ITenantDbContext
{
    public const string Schema = "contacts";

    public Guid CurrentTenantId => tenantContext.TenantId;

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<ContactIdentity> Identities => Set<ContactIdentity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Contact>(contact =>
        {
            contact.ToTable("contacts");
            contact.Property(c => c.Name).HasMaxLength(Contact.NameMaxLength);
            contact.Property(c => c.Phone).HasMaxLength(Contact.PhoneMaxLength);
            contact.Property(c => c.Email).HasMaxLength(Contact.EmailMaxLength);
            contact.Property(c => c.Language).HasMaxLength(Contact.LanguageMaxLength);
            contact.Property(c => c.Notes).HasMaxLength(Contact.NotesMaxLength);
            contact.HasIndex(c => c.TenantId);
        });

        modelBuilder.Entity<ContactIdentity>(identity =>
        {
            identity.ToTable("contact_identities");
            identity.Property(i => i.Channel).HasMaxLength(ContactIdentity.ChannelMaxLength);
            identity.Property(i => i.ExternalId).HasMaxLength(ContactIdentity.ExternalIdMaxLength);
            identity.HasOne<Contact>().WithMany().HasForeignKey(i => i.ContactId).OnDelete(DeleteBehavior.Cascade);
            // One contact per channel address per workspace; also guards concurrent first messages.
            identity.HasIndex(i => new { i.TenantId, i.Channel, i.ExternalId }).IsUnique();
            identity.HasIndex(i => i.ContactId);
        });

        modelBuilder.ApplyTenantQueryFilters(this);
    }
}

/// <summary>Used by <c>dotnet ef</c> with the schema-owner connection (docs/standards/database.md).</summary>
internal sealed class ContactsDbContextFactory : IDesignTimeDbContextFactory<ContactsDbContext>
{
    public ContactsDbContext CreateDbContext(string[] args) =>
        Create(Environment.GetEnvironmentVariable("ConnectionStrings__" + PersistenceServiceCollectionExtensions.MigrationsConnectionName)
            // Placeholder so `migrations add` works offline; `database update` takes --connection.
            ?? "Host=localhost;Database=nihasdesk;Username=nihasdesk");

    public static ContactsDbContext Create(string migrationsConnectionString)
    {
        var options = new DbContextOptionsBuilder<ContactsDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureNpgsql(options, migrationsConnectionString, ContactsDbContext.Schema);
        return new ContactsDbContext(options.Options, new TenantContext());
    }
}
