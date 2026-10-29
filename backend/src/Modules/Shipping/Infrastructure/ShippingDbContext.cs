using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Shipping.Contracts;
using Nihasdesk.Modules.Shipping.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Shipping.Infrastructure;

/// <summary>Shipping data in schema <c>shipping</c>; every table is tenant-owned (query filters + RLS).</summary>
internal sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options, ITenantContext tenantContext)
    : DbContext(options), ITenantDbContext
{
    public const string Schema = "shipping";

    public Guid CurrentTenantId => tenantContext.TenantId;

    public DbSet<ShippingZone> Zones => Set<ShippingZone>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ShippingZone>(zone =>
        {
            zone.ToTable("zones", table =>
            {
                table.HasCheckConstraint("ck_zones_price", "price >= 0");
                table.HasCheckConstraint("ck_zones_free_over", "free_over IS NULL OR free_over >= 0");
                table.HasCheckConstraint("ck_zones_currency", "currency ~ '^[A-Z]{3}$'");
            });
            zone.Property(z => z.Id).ValueGeneratedNever();
            zone.Property(z => z.Name).HasMaxLength(ShippingZone.NameMaxLength);
            zone.Property(z => z.Areas).HasMaxLength(ShippingZone.AreasMaxLength);
            zone.Property(z => z.Currency).HasMaxLength(ShippingZone.CurrencyLength).IsFixedLength();
            zone.HasIndex(z => new { z.TenantId, z.Position });
        });

        modelBuilder.ApplyTenantQueryFilters(this);
    }
}

internal sealed class ShippingRates(ShippingDbContext db) : IShippingRates
{
    public async Task<IReadOnlyList<ShippingRate>> ListAsync(CancellationToken cancellationToken) =>
        await db.Zones
            .AsNoTracking()
            .OrderBy(z => z.Position)
            .Select(z => new ShippingRate(z.Id, z.Name, z.Areas, z.Price, z.Currency, z.FreeOver, z.MinDays, z.MaxDays))
            .ToListAsync(cancellationToken);
}

/// <summary>Used by <c>dotnet ef</c> with the schema-owner connection (docs/standards/database.md).</summary>
internal sealed class ShippingDbContextFactory : IDesignTimeDbContextFactory<ShippingDbContext>
{
    public ShippingDbContext CreateDbContext(string[] args) =>
        Create(Environment.GetEnvironmentVariable("ConnectionStrings__" + PersistenceServiceCollectionExtensions.MigrationsConnectionName)
            // Placeholder so `migrations add` works offline; `database update` takes --connection.
            ?? "Host=localhost;Database=nihasdesk;Username=nihasdesk");

    public static ShippingDbContext Create(string migrationsConnectionString)
    {
        var options = new DbContextOptionsBuilder<ShippingDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureNpgsql(options, migrationsConnectionString, ShippingDbContext.Schema);
        return new ShippingDbContext(options.Options, new TenantContext());
    }
}
