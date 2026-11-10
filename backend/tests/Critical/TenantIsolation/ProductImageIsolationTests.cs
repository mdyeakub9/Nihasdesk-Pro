using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Features.Images;
using Nihasdesk.Modules.Commerce.Infrastructure;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for product images (Issue #86, ADR 0031 §7): one business never sees another's
/// photos, and a signed public link can't be forged, moved to another workspace, or used to open
/// customer media.
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class ProductImageIsolationTests(ModulesPostgresFixture fixture)
{
    private static readonly ProductImageSigner Signer = SignerWith(Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));

    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_images_even_without_the_EF_filter()
    {
        await SeedAsync();
        await using var scope = fixture.Scope(A);

        var rows = await scope.Get<CommerceDbContext>().ProductImages.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, i => Assert.Equal(A, i.TenantId));
    }

    [Fact]
    public async Task Writing_an_image_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<CommerceDbContext>();

        db.ProductImages.Add(Image(B, fixture.TenantB.ProductId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task No_link_is_made_for_another_tenants_image()
    {
        var imageOfB = (await SeedAsync())[B];
        await using var scope = fixture.Scope(A);
        var links = new ProductImageLinks(
            Signer,
            scope.Get<CommerceDbContext>(),
            scope.Get<TenantContext>(),
            Options.Create(new ProductImageOptions()),
            TimeProvider.System);

        Assert.Null(await links.CardUrlAsync(imageOfB, CancellationToken.None));
        Assert.NotNull(await links.CardUrlAsync((await SeedAsync())[A], CancellationToken.None));
    }

    [Fact]
    public async Task A_signed_link_opens_its_own_image_and_nothing_else()
    {
        var images = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var scopes = await ScopeFactoryAsync();
        var token = Signer.Sign(B, images[B], now.AddDays(7));

        Assert.NotNull(await ProductImageEndpoints.FindPublicAsync(token, Signer, scopes, now, CancellationToken.None));

        // B's image under A's name, or A's image under B's name: the database finds nothing either way.
        Assert.Null(await ProductImageEndpoints.FindPublicAsync(Signer.Sign(A, images[B], now.AddDays(7)), Signer, scopes, now, CancellationToken.None));
        Assert.Null(await ProductImageEndpoints.FindPublicAsync(Signer.Sign(B, images[A], now.AddDays(7)), Signer, scopes, now, CancellationToken.None));

        // A customer photo's ID, even correctly signed, opens nothing: the link reads product images only.
        Assert.Null(await ProductImageEndpoints.FindPublicAsync(Signer.Sign(A, await CustomerPhotoAsync(), now.AddDays(7)), Signer, scopes, now, CancellationToken.None));

        // Expired, signed with another key, or changed in any byte: refused before the database.
        Assert.Null(await ProductImageEndpoints.FindPublicAsync(token, Signer, scopes, now.AddDays(8), CancellationToken.None));
        var otherKey = SignerWith(Convert.ToBase64String(new byte[32]));
        Assert.Null(await ProductImageEndpoints.FindPublicAsync(otherKey.Sign(B, images[B], now.AddDays(7)), Signer, scopes, now, CancellationToken.None));
        var bytes = System.Buffers.Text.Base64Url.DecodeFromChars(token);
        for (var i = 0; i < bytes.Length; i++)
        {
            var changed = (byte[])bytes.Clone();
            changed[i] ^= 0x01;
            var forged = System.Buffers.Text.Base64Url.EncodeToString(changed);
            Assert.False(Signer.TryVerify(forged, now, out _, out _), $"A change in byte {i} was accepted.");
        }
    }

    [Fact]
    public async Task Database_isolates_the_table()
    {
        await SeedAsync();
        await TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "commerce", "product_images");
    }

    /// <summary>One saved image per tenant, on its seeded product; returns their IDs.</summary>
    private async Task<Dictionary<Guid, Guid>> SeedAsync()
    {
        var ids = new Dictionary<Guid, Guid>();
        foreach (var (tenant, product) in new[] { (A, fixture.TenantA.ProductId), (B, fixture.TenantB.ProductId) })
        {
            await using var scope = fixture.Scope(tenant);
            var db = scope.Get<CommerceDbContext>();
            var image = await db.ProductImages.FirstOrDefaultAsync(i => i.ProductId == product);
            if (image is null)
            {
                image = Image(tenant, product);
                db.ProductImages.Add(image);
                await db.SaveChangesAsync();
            }

            ids[tenant] = image.Id;
        }

        return ids;
    }

    private async Task<Guid> CustomerPhotoAsync()
    {
        await using var scope = fixture.Scope(A);
        await scope.Get<IInboxIngest>().ReceiveAsync(
            new InboundMessage(
                ChannelKind.Messenger,
                $"{A:N}-product-image-test",
                "Giulia Rossi",
                string.Empty,
                $"{A:N}-product-image-test-{Guid.NewGuid():N}",
                DateTimeOffset.UtcNow,
                null,
                [new InboundAttachment(InboundAttachmentKind.Image, "https://cdn.fbsbx.com/photo.jpg")]),
            CancellationToken.None);
        return await scope.Get<InboxDbContext>().Attachments.OrderByDescending(a => a.CreatedAt).Select(a => a.Id).FirstAsync();
    }

    /// <summary>The root factory, as the endpoint gets it: each lookup makes its own scope.</summary>
    private async Task<IServiceScopeFactory> ScopeFactoryAsync()
    {
        await using var scope = fixture.Scope(null);
        return scope.Get<IServiceScopeFactory>();
    }

    private static ProductImage Image(Guid tenant, Guid product) =>
        ProductImage.Uploaded(tenant, product, 1, "image/jpeg", 100, 10, 10, new string('0', 64), $"{tenant:N}/{Guid.NewGuid():N}", $"{tenant:N}/{Guid.NewGuid():N}", $"{tenant:N}/{Guid.NewGuid():N}");

    private static ProductImageSigner SignerWith(string key) =>
        new(Options.Create(new ProductImageOptions { SigningKey = key, PublicUrl = "https://public.example.test" }));
}
