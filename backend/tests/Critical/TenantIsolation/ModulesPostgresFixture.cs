using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.Channels;
using Nihasdesk.Modules.Channels.Domain;
using Nihasdesk.Modules.Channels.Infrastructure;
using Nihasdesk.Modules.Commerce;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Infrastructure;
using Nihasdesk.Modules.Contacts;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Contacts.Infrastructure;
using Nihasdesk.Modules.Identity;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Domain;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Inbox;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Nihasdesk.Modules.Shipping;
using Nihasdesk.Modules.Shipping.Domain;
using Nihasdesk.Modules.Shipping.Infrastructure;
using Nihasdesk.SharedKernel;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// Every module on one real PostgreSQL, migrated as the schema owner, used as the non-owner app
/// role (SEC-11). Two workspaces get a row in every tenant-owned table, created the way the
/// product creates them, so isolation tests are never empty.
/// </summary>
public sealed class ModulesPostgresFixture : IAsyncLifetime
{
    private const string AppRolePassword = "critical-tests-only";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("nihasdesk")
        .WithUsername("nihasdesk")
        .WithPassword("critical-tests-owner")
        .Build();

    private ServiceProvider? _services;

    internal ModuleSeed TenantA { get; private set; } = null!;

    internal ModuleSeed TenantB { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var ownerConnectionString = _container.GetConnectionString();

        await using (var owner = new NpgsqlConnection(ownerConnectionString))
        {
            await owner.OpenAsync();
            await using var createRole = new NpgsqlCommand(
                $"CREATE ROLE {RlsMigrationExtensions.AppRole} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS",
                owner);
            await createRole.ExecuteNonQueryAsync();
        }

        DbContext[] migrators =
        [
            IdentityModuleDbContextFactory.Create(ownerConnectionString),
            ContactsDbContextFactory.Create(ownerConnectionString),
            InboxDbContextFactory.Create(ownerConnectionString),
            ChannelsDbContextFactory.Create(ownerConnectionString),
            CommerceDbContextFactory.Create(ownerConnectionString),
            AiDbContextFactory.Create(ownerConnectionString),
            ShippingDbContextFactory.Create(ownerConnectionString),
        ];
        foreach (var migrator in migrators)
        {
            await using (migrator)
            {
                await migrator.Database.MigrateAsync();
            }
        }

        // One pooled connection, so tests also prove a tenant never leaks to the next user of it.
        var appConnectionString = new NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = RlsMigrationExtensions.AppRole,
            Password = AppRolePassword,
            MaxPoolSize = 1,
        }.ConnectionString;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{PersistenceServiceCollectionExtensions.AppConnectionName}"] = appConnectionString,
                // No background processors: nothing runs except what a test calls.
                ["Channels:BackgroundProcessing"] = "false",
                ["Ai:BackgroundProcessing"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        IModule[] modules =
        [
            new IdentityModule(),
            new ContactsModule(),
            new InboxModule(),
            new ChannelsModule(),
            new CommerceModule(),
            new AiAgentModule(),
            new ShippingModule(),
        ];
        foreach (var module in modules)
        {
            module.AddServices(services, configuration);
        }

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        TenantA = await SeedTenantAsync("Almanaam", "modules-a@example.com", "page-a");
        TenantB = await SeedTenantAsync("Bottega Blu", "modules-b@example.com", "page-b");
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>A request scope; <paramref name="tenantId"/> plays the authenticated tenant.</summary>
    internal ModulesScope Scope(Guid? tenantId)
    {
        var scope = _services!.CreateAsyncScope();
        if (tenantId is { } id)
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(id);
        }

        return new ModulesScope(scope);
    }

    private async Task<ModuleSeed> SeedTenantAsync(string name, string email, string externalId)
    {
        var organization = Organization.Create(name);
        var workspace = Workspace.Create(organization.Id, name);
        var owner = new User { UserName = email, Email = email, DisplayName = name };
        var tenantId = workspace.Id;
        var now = DateTimeOffset.UtcNow;

        await using (var scope = Scope(tenantId))
        {
            var identity = scope.Get<IdentityModuleDbContext>();
            identity.AddRange(organization, workspace, owner, Membership.Create(tenantId, owner.Id, Role.Owner));
            await identity.SaveChangesAsync();
        }

        await using var seed = Scope(tenantId);

        // Contacts + Inbox: a customer message, the way a channel delivers it.
        var inbound = await seed.Get<IInboxIngest>().ReceiveAsync(
            new InboundMessage(ChannelKind.Messenger, externalId + "-customer", "Giulia Rossi", "Ciao, avete la borsa nera?", externalId + "-m1", now),
            CancellationToken.None);
        var inbox = seed.Get<InboxDbContext>();
        var contactId = (await inbox.Conversations.SingleAsync(c => c.Id == inbound.ConversationId)).ContactId;
        var noteText = $"Team note for {name} only";
        inbox.AddRange(
            Message.TeamNote(tenantId, inbound.ConversationId, owner.Id, name, noteText, [], now),
            Message.AiNote(tenantId, inbound.ConversationId, "Handed over: asks for a discount", now),
            ReadMarker.Create(tenantId, inbound.ConversationId, owner.Id, now),
            SavedReply.Create(tenantId, "ciao", "Greeting", "Hi!", "Ciao!"));
        await inbox.SaveChangesAsync();

        // Channels: a connected Page.
        var channels = seed.Get<ChannelsDbContext>();
        var connection = ChannelConnection.Connect(tenantId, ChannelKind.Messenger, externalId, name, "protected-token", owner.Id, now);
        channels.Add(connection);
        await channels.SaveChangesAsync();

        // Shipping + Commerce: a zone and a product the AI can sell.
        var shipping = seed.Get<ShippingDbContext>();
        var zone = ShippingZone.Create(tenantId, 0, "Sardegna", "Sassari", 700, "EUR", null, 2, 4);
        shipping.Add(zone);
        await shipping.SaveChangesAsync();

        var commerce = seed.Get<CommerceDbContext>();
        var product = Product.Create(
            tenantId,
            "borsa-nera",
            new ProductData($"Borsa nera {name}", null, ProductStatus.Active, null, null, null, [], []));
        var variant = ProductVariant.Create(
            tenantId,
            product.Id,
            new VariantData("BORSA-1", null, null, null, null, 8900, null, 4000, "EUR", true, 10, InventoryPolicy.Deny, null, true),
            0);
        commerce.AddRange(product, variant);
        await commerce.SaveChangesAsync();

        // Commerce: the AI's order request for that chat (orders + order_items).
        var request = await seed.Get<IOrderRequests>().CreateAsync(
            inbound.ConversationId,
            [new OrderRequestLine(product.Id, variant.Id, 1)],
            new OrderRequestDetails("Giulia Rossi", "+39 333 1234567", "Via Roma 12", "07100", "Sassari", "SS", "IT", zone.Id, null),
            CancellationToken.None);
        Assert.True(request.IsSuccess, "Seeding the order request failed.");

        // AiAgent: settings, policies, an unanswered question, a taught answer, a run.
        var ai = seed.Get<AiDbContext>();
        ai.AddRange(
            AiSettings.Defaults(tenantId),
            ShopPolicies.Empty(tenantId),
            KnowledgeGap.Record(tenantId, inbound.ConversationId, "Fate sconti?"),
            TaughtAnswer.Create(tenantId, "Orari?", "Lun–Sab 9–19", owner.Id),
            AiRun.Finish(tenantId, inbound.ConversationId, "test-model", 10, 5, [], RunOutcome.Replied, null, TimeSpan.FromSeconds(1)));
        await ai.SaveChangesAsync();

        return new ModuleSeed(
            tenantId,
            owner.Id,
            contactId,
            inbound.ConversationId,
            connection.Id,
            zone.Id,
            product.Id,
            variant.Id,
            request.Value.OrderId,
            noteText);
    }
}

internal sealed record ModuleSeed(
    Guid TenantId,
    Guid OwnerId,
    Guid ContactId,
    Guid ConversationId,
    Guid ConnectionId,
    Guid ZoneId,
    Guid ProductId,
    Guid VariantId,
    Guid OrderId,
    string NoteText);

internal sealed class ModulesScope(AsyncServiceScope scope) : IAsyncDisposable
{
    public T Get<T>()
        where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class ModulesPostgresTests : ICollectionFixture<ModulesPostgresFixture>
{
    public const string Name = "postgres-modules";
}
