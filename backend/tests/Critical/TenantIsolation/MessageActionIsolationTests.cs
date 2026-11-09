using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for rich-message buttons (Issue #91, ADR 0032 §3): a button token of one business
/// never acts in another, and a token of one conversation never acts in another one (SEC-36).
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class MessageActionIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_buttons_even_without_the_EF_filter()
    {
        await SeedAsync(A);
        await SeedAsync(B);
        await using var scope = fixture.Scope(A);

        var rows = await scope.Get<InboxDbContext>().MessageActions.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, a => Assert.Equal(A, a.TenantId));
    }

    [Fact]
    public async Task Writing_a_button_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<InboxDbContext>();

        db.MessageActions.Add(MessageAction.Create(B, Guid.NewGuid(), Guid.NewGuid(), 0, RichAction.TalkToHuman, "Parla con noi", null, null, null, DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_isolates_the_table()
    {
        await SeedAsync(A);
        await TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "inbox", "message_actions");
    }

    [Fact]
    public async Task A_token_of_workspace_A_received_by_workspace_B_is_only_text()
    {
        var (payload, contact) = await SeedAsync(A);

        await using var scope = fixture.Scope(B);
        var received = await scope.Get<IInboxIngest>().ReceiveAsync(Tap(contact, payload), CancellationToken.None);

        var stored = await scope.Get<InboxDbContext>().Messages.AsNoTracking().SingleAsync(m => m.Id == received.MessageId);
        Assert.Equal(MessageKind.Text, stored.Kind);
        Assert.Null(stored.ActionId);
    }

    [Fact]
    public async Task A_token_of_one_conversation_used_in_another_is_only_text()
    {
        var (payload, _) = await SeedAsync(A);

        await using var scope = fixture.Scope(A);
        var received = await scope.Get<IInboxIngest>().ReceiveAsync(Tap($"{A:N}-other-{Guid.NewGuid():N}", payload), CancellationToken.None);

        var db = scope.Get<InboxDbContext>();
        var stored = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == received.MessageId);
        Assert.Equal(MessageKind.Text, stored.Kind);
        Assert.Null(stored.ActionId);
        var token = payload[MessageAction.PayloadPrefix.Length..];
        Assert.Null((await db.MessageActions.AsNoTracking().SingleAsync(a => a.Token == token)).UsedAt);
    }

    [Fact]
    public async Task The_same_token_in_its_own_conversation_is_a_tap()
    {
        var (payload, contact) = await SeedAsync(A);

        await using var scope = fixture.Scope(A);
        var received = await scope.Get<IInboxIngest>().ReceiveAsync(Tap(contact, payload), CancellationToken.None);

        var stored = await scope.Get<InboxDbContext>().Messages.AsNoTracking().SingleAsync(m => m.Id == received.MessageId);
        Assert.Equal(MessageKind.Tap, stored.Kind);
        Assert.NotNull(stored.ActionId);
    }

    private static InboundMessage Tap(string contact, string payload) =>
        new(ChannelKind.Messenger, contact, "Giulia Rossi", "Parla con noi", $"tap-{Guid.NewGuid():N}", DateTimeOffset.UtcNow, null, null, payload);

    /// <summary>A conversation of <paramref name="tenant"/> with one button; returns its payload and the customer's channel id.</summary>
    private async Task<(string Payload, string Contact)> SeedAsync(Guid tenant)
    {
        var contact = $"{tenant:N}-rich-{Guid.NewGuid():N}";
        await using var scope = fixture.Scope(tenant);
        var received = await scope.Get<IInboxIngest>().ReceiveAsync(
            new InboundMessage(ChannelKind.Messenger, contact, "Giulia Rossi", "Ciao", $"{contact}-m1", DateTimeOffset.UtcNow),
            CancellationToken.None);
        var db = scope.Get<InboxDbContext>();
        var action = MessageAction.Create(
            tenant, received.ConversationId, received.MessageId, 0, RichAction.TalkToHuman, "Parla con noi", null, null, null, DateTimeOffset.UtcNow);
        db.MessageActions.Add(action);
        await db.SaveChangesAsync();
        return (action.Payload, contact);
    }
}
