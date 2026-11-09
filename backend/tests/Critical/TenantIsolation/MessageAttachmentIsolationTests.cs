using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>CLAUDE.md rule 1 for customer media (Issue #84, ADR 0031 §1): one business never sees another's photos and files.</summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class MessageAttachmentIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_media_even_without_the_EF_filter()
    {
        await SeedAsync();
        await using var scope = fixture.Scope(A);

        var rows = await scope.Get<InboxDbContext>().Attachments.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, a => Assert.Equal(A, a.TenantId));
    }

    [Fact]
    public async Task The_lookup_behind_the_media_endpoint_finds_nothing_of_another_tenant()
    {
        await SeedAsync();
        Guid idOfB;
        await using (var scope = fixture.Scope(B))
        {
            idOfB = await scope.Get<InboxDbContext>().Attachments.Select(a => a.Id).FirstAsync();
        }

        await using var other = fixture.Scope(A);
        var db = other.Get<InboxDbContext>();

        Assert.Null(await db.Attachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == idOfB));
        Assert.Null(await db.Attachments.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.Id == idOfB));
    }

    [Fact]
    public async Task Writing_media_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<InboxDbContext>();

        db.Attachments.Add(MessageAttachment.Incoming(B, Guid.NewGuid(), 0, AttachmentKind.Image, "https://cdn.fbsbx.com/x", DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_isolates_the_table()
    {
        await SeedAsync();
        await TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "inbox", "message_attachments");
    }

    private async Task SeedAsync()
    {
        foreach (var tenant in new[] { A, B })
        {
            await using var scope = fixture.Scope(tenant);
            if (!await scope.Get<InboxDbContext>().Attachments.AnyAsync())
            {
                await scope.Get<IInboxIngest>().ReceiveAsync(
                    new InboundMessage(
                        ChannelKind.Messenger,
                        $"{tenant:N}-media",
                        "Giulia Rossi",
                        string.Empty,
                        $"{tenant:N}-media-m1",
                        DateTimeOffset.UtcNow,
                        null,
                        [new InboundAttachment(InboundAttachmentKind.Image, "https://cdn.fbsbx.com/photo.jpg")]),
                    CancellationToken.None);
            }
        }
    }
}
