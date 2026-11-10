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

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

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
            // Removing an image leaves its variants on the main image.
            variant.HasOne<ProductImage>().WithMany().HasForeignKey(v => v.ImageId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProductImage>(image =>
        {
            image.ToTable("product_images", table =>
                table.HasCheckConstraint("ck_product_images_position", $"position BETWEEN 1 AND {ProductImage.MaxPerProduct}"));
            image.Property(i => i.Id).ValueGeneratedNever();
            LowerCaseText(image.Property(i => i.Status));
            image.Property(i => i.FailureCode).HasMaxLength(ProductImageFailure.MaxLength);
            image.Property(i => i.SourceUrl).HasMaxLength(ProductImage.SourceUrlMaxLength);
            image.Property(i => i.ContentType).HasMaxLength(ProductImage.ContentTypeMaxLength);
            image.Property(i => i.Sha256).HasMaxLength(64);
            image.Property(i => i.CardKey).HasMaxLength(ProductImage.KeyMaxLength);
            image.Property(i => i.ThumbnailKey).HasMaxLength(ProductImage.KeyMaxLength);
            image.Property(i => i.AiImageKey).HasMaxLength(ProductImage.KeyMaxLength);
            image.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
            // Not unique: a reorder moves several rows in one save.
            image.HasIndex(i => new { i.ProductId, i.Position });
            // The download job's queue.
            image.HasIndex(i => i.NextAttemptAt).HasFilter("status = 'pending'");
        });

        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("orders", table =>
            {
                table.HasCheckConstraint("ck_orders_amounts", "subtotal >= 0 AND shipping_cost >= 0 AND total = subtotal + shipping_cost");
                table.HasCheckConstraint("ck_orders_currency", "currency ~ '^[A-Z]{3}$'");
            });
            order.Property(o => o.Id).ValueGeneratedNever();
            order.Property(o => o.CustomerName).HasMaxLength(Order.NameMaxLength);
            order.Property(o => o.Phone).HasMaxLength(Order.PhoneMaxLength);
            order.Property(o => o.Street).HasMaxLength(Order.StreetMaxLength);
            order.Property(o => o.PostalCode).HasMaxLength(Order.PostalCodeMaxLength);
            order.Property(o => o.City).HasMaxLength(Order.CityMaxLength);
            order.Property(o => o.Province).HasMaxLength(Order.ProvinceMaxLength);
            order.Property(o => o.Country).HasMaxLength(2).IsFixedLength();
            order.Property(o => o.Notes).HasMaxLength(Order.NotesMaxLength);
            order.Property(o => o.ShippingZoneName).HasMaxLength(Order.ZoneNameMaxLength);
            order.Property(o => o.Currency).HasMaxLength(ProductVariant.CurrencyLength).IsFixedLength();
            order.Property(o => o.PaymentMethod).HasMaxLength(20);
            order.Property(o => o.RejectReason).HasMaxLength(Order.ReasonMaxLength);
            LowerCaseText(order.Property(o => o.Status));
            LowerCaseText(order.Property(o => o.Source));
            order.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            order.Navigation(o => o.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
            // #1001, #1002… per workspace (ADR 0025).
            order.HasIndex(o => new { o.TenantId, o.Number }).IsUnique();
            // The Orders page: per status, newest first, keyset pagination.
            order.HasIndex(o => new { o.TenantId, o.Status, o.CreatedAt, o.Id }).IsDescending(false, false, true, true);
        });

        modelBuilder.Entity<OrderItem>(item =>
        {
            item.ToTable("order_items", table =>
                table.HasCheckConstraint("ck_order_items_quantity", "quantity > 0 AND unit_price >= 0 AND line_total = unit_price * quantity"));
            item.Property(i => i.Id).ValueGeneratedNever();
            item.Property(i => i.Title).HasMaxLength(OrderItem.TitleMaxLength);
            item.Property(i => i.Variant).HasMaxLength(OrderItem.VariantMaxLength);
            item.Property(i => i.Sku).HasMaxLength(ProductVariant.SkuMaxLength);
            item.HasIndex(i => i.OrderId);
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
