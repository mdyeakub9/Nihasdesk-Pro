using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for the Commerce module (catalog, orders, order requests), and rule 5: the AI
/// only ever creates a pending order request from its own workspace's catalog, chat and zones.
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class CommerceIsolationTests(ModulesPostgresFixture fixture)
{
    private static readonly OrderRequestDetails Details =
        new("Giulia Rossi", "+39 333 1234567", "Via Roma 12", "07100", "Sassari", "SS", "IT", Guid.Empty, null);

    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_rows_even_without_the_EF_filter()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<CommerceDbContext>();

        Assert.All(await db.Products.IgnoreQueryFilters().ToListAsync(), p => Assert.Equal(A, p.TenantId));
        Assert.All(await db.Variants.IgnoreQueryFilters().ToListAsync(), v => Assert.Equal(A, v.TenantId));
        Assert.All(await db.Orders.IgnoreQueryFilters().ToListAsync(), o => Assert.Equal(A, o.TenantId));
        Assert.All(await db.OrderItems.IgnoreQueryFilters().ToListAsync(), i => Assert.Equal(A, i.TenantId));
        Assert.Null(await db.Orders.SingleOrDefaultAsync(o => o.Id == fixture.TenantB.OrderId));
        Assert.Null(await db.Orders.IgnoreQueryFilters().SingleOrDefaultAsync(o => o.Id == fixture.TenantB.OrderId));
    }

    [Fact]
    public async Task Writing_a_product_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<CommerceDbContext>();

        db.Products.Add(Product.Create(B, "intruder", new ProductData("Intruder", null, ProductStatus.Active, null, null, null, [], [])));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_AI_catalog_shows_only_the_current_tenants_products()
    {
        await using var scope = fixture.Scope(A);
        var catalog = scope.Get<ICatalog>();

        var found = await catalog.SearchAsync("borsa", 20, CancellationToken.None);

        Assert.Contains(found, p => p.Id == fixture.TenantA.ProductId);
        Assert.DoesNotContain(found, p => p.Id == fixture.TenantB.ProductId);
        Assert.Null(await catalog.GetAsync(fixture.TenantB.ProductId, CancellationToken.None));
    }

    [Fact]
    public async Task The_AI_cannot_quote_or_request_with_another_tenants_product_zone_or_chat()
    {
        var own = fixture.TenantA;
        var other = fixture.TenantB;
        await using var scope = fixture.Scope(A);
        var requests = scope.Get<IOrderRequests>();

        var otherProduct = await requests.QuoteAsync([new OrderRequestLine(other.ProductId, other.VariantId, 1)], own.ZoneId, CancellationToken.None);
        var otherZone = await requests.QuoteAsync([new OrderRequestLine(own.ProductId, own.VariantId, 1)], other.ZoneId, CancellationToken.None);
        var otherChat = await requests.CreateAsync(
            other.ConversationId,
            [new OrderRequestLine(own.ProductId, own.VariantId, 1)],
            Details with { ShippingZoneId = own.ZoneId },
            CancellationToken.None);

        Assert.False(otherProduct.IsSuccess);
        Assert.False(otherZone.IsSuccess);
        Assert.False(otherChat.IsSuccess);
        Assert.Equal("conversation.not_found", otherChat.Error.Code);

        await using var tenantB = fixture.Scope(B);
        Assert.Single(await tenantB.Get<CommerceDbContext>().Orders.Where(o => o.ConversationId == other.ConversationId).ToListAsync());
    }

    [Fact]
    public async Task The_AI_order_path_only_ever_creates_a_pending_request()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<CommerceDbContext>();

        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == fixture.TenantA.OrderId);
        var variant = await db.Variants.AsNoTracking().SingleAsync(v => v.Id == fixture.TenantA.VariantId);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(OrderSource.Ai, order.Source);
        Assert.Null(order.DecidedAt);
        Assert.Null(order.DecidedByUserId);
        // No stock is taken until a person approves (seeded with 10).
        Assert.Equal(10, variant.Stock);

        // The AI's contract has no way to confirm, approve or edit an order.
        var methods = typeof(IOrderRequests).GetMethods().Select(m => m.Name).Order().ToArray();
        Assert.Equal(["CreateAsync", "QuoteAsync"], methods);
    }

    [Theory]
    [InlineData("products")]
    [InlineData("product_variants")]
    [InlineData("orders")]
    [InlineData("order_items")]
    public Task Database_isolates_the_table(string table) =>
        TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "commerce", table);
}
