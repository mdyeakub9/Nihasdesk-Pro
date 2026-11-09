using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for deleting media (SEC-45, Issue #84 PR 3): erasing one business's media never
/// touches another's, even when it is handed another business's contact.
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class MediaErasureIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Erasing_a_contact_of_another_tenant_deletes_nothing()
    {
        await SeedAsync(A, "contact-a");
        var contactOfB = await SeedAsync(B, "contact-b");
        var before = await CountAsync(B);

        await using (var scope = fixture.Scope(A))
        {
            Assert.Equal(0, await scope.Get<IMediaErasure>().EraseContactAsync(contactOfB, CancellationToken.None));
        }

        Assert.Equal(before, await CountAsync(B));
    }

    [Fact]
    public async Task Erasing_a_workspace_deletes_only_its_own_media()
    {
        await SeedAsync(A, "workspace-a");
        await SeedAsync(B, "workspace-b");
        var ofB = await CountAsync(B);

        await using (var scope = fixture.Scope(A))
        {
            Assert.True(await scope.Get<IMediaErasure>().EraseWorkspaceAsync(CancellationToken.None) > 0);
        }

        Assert.Equal(0, await CountAsync(A));
        Assert.Equal(ofB, await CountAsync(B));
        Assert.True(ofB > 0);
    }

    private async Task<int> CountAsync(Guid tenant)
    {
        await using var scope = fixture.Scope(tenant);
        return await scope.Get<InboxDbContext>().Attachments.CountAsync();
    }

    /// <summary>A customer photo in a new conversation; returns the conversation's contact.</summary>
    private async Task<Guid> SeedAsync(Guid tenant, string name)
    {
        await using var scope = fixture.Scope(tenant);
        var psid = $"{tenant:N}-{name}-{Guid.NewGuid():N}";
        await scope.Get<IInboxIngest>().ReceiveAsync(
            new InboundMessage(
                ChannelKind.Messenger,
                psid,
                "Giulia Rossi",
                string.Empty,
                $"{psid}-m1",
                DateTimeOffset.UtcNow,
                null,
                [new InboundAttachment(InboundAttachmentKind.Image, "https://cdn.fbsbx.com/photo.jpg")]),
            CancellationToken.None);

        var db = scope.Get<InboxDbContext>();
        var conversationId = await db.Messages.Where(m => m.ExternalId == $"{psid}-m1").Select(m => m.ConversationId).SingleAsync();
        return await db.Conversations.Where(c => c.Id == conversationId).Select(c => c.ContactId).SingleAsync();
    }
}
