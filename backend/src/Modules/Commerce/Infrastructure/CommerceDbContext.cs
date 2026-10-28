using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Infrastructure;

/// <summary>Commerce data in schema <c>commerce</c>; every table is tenant-owned (query filters + RLS).</summary>
internal sealed class CommerceDbContext(DbContextOptions<CommerceDbContext> options, ITenantContext tenantContext)
    : DbContext(options), ITenantDbContext
{
    public const string Schema = "commerce";

    public Guid CurrentTenantId => tenantContext.TenantId;

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductVariant> Variants => Set<ProductVariant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products");
            // IDs are set in code (Guid v7); variants added through the product must be INSERTed, not UPDATEd.
            product.Property(p => p.Id).ValueGeneratedNever();
            product.Property(p => p.Title).HasMaxLength(Product.TitleMaxLength);
            product.Property(p => p.Handle).HasMaxLength(Product.HandleMaxLength);
            product.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
            product.Property(p => p.ProductType).HasMaxLength(Product.TypeMaxLength);
            product.Property(p => p.Category).HasMaxLength(Product.CategoryMaxLength);
            product.Property(p => p.Vendor).HasMaxLength(Product.VendorMaxLength);
            LowerCaseText(product.Property(p => p.Status));
            product.HasMany(p => p.Variants).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
            product.Navigation(p => p.Variants).HasField("_variants").UsePropertyAccessMode(PropertyAccessMode.Field);
            product.HasIndex(p => new { p.TenantId, p.Handle }).IsUnique();
            // The product list: newest first, keyset pagination.
            product.HasIndex(p => new { p.TenantId, p.CreatedAt, p.Id }).IsDescending(false, true, true);
        });

        modelBuilder.Entity<ProductVariant>(variant =>
        {
            variant.ToTable("product_variants", table =>
            {
                table.HasCheckConstraint("ck_product_variants_price", "price >= 0");
                table.HasCheckConstraint("ck_product_variants_currency", "currency ~ '^[A-Z]{3}$'");
            });
            variant.Property(v => v.Id).ValueGeneratedNever();
            variant.Property(v => v.Sku).HasMaxLength(ProductVariant.SkuMaxLength);
            variant.Property(v => v.Barcode).HasMaxLength(ProductVariant.BarcodeMaxLength);
            variant.Property(v => v.Option1).HasMaxLength(ProductVariant.OptionValueMaxLength);
            variant.Property(v => v.Option2).HasMaxLength(ProductVariant.OptionValueMaxLength);
            variant.Property(v => v.Option3).HasMaxLength(ProductVariant.OptionValueMaxLength);
            variant.Property(v => v.Currency).HasMaxLength(ProductVariant.CurrencyLength).IsFixedLength();
            LowerCaseText(variant.Property(v => v.InventoryPolicy));
            variant.Ignore(v => v.Available);
            // Update-by-SKU on import (ADR 0018): one SKU per workspace.
            variant.HasIndex(v => new { v.TenantId, v.Sku }).IsUnique().HasFilter("sku IS NOT NULL");
            variant.HasIndex(v => v.ProductId);
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
internal sealed class CommerceDbContextFactory : IDesignTimeDbContextFactory<CommerceDbContext>
{
    public CommerceDbContext CreateDbContext(string[] args) =>
        Create(Environment.GetEnvironmentVariable("ConnectionStrings__" + PersistenceServiceCollectionExtensions.MigrationsConnectionName)
            // Placeholder so `migrations add` works offline; `database update` takes --connection.
            ?? "Host=localhost;Database=nihasdesk;Username=nihasdesk");

    public static CommerceDbContext Create(string migrationsConnectionString)
    {
        var options = new DbContextOptionsBuilder<CommerceDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureNpgsql(options, migrationsConnectionString, CommerceDbContext.Schema);
        return new CommerceDbContext(options.Options, new TenantContext());
    }
}
