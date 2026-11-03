using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for the Inbox module, and what the AI may read of a chat: its own workspace's
/// conversations only, and never the team's internal notes (rule 7, SEC-37).
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class InboxIsolationTests(ModulesPostgresFixture fixture)
{
    private const int HistorySize = 50;

    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_rows_even_without_the_EF_filter()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<InboxDbContext>();

        Assert.All(await db.Conversations.IgnoreQueryFilters().ToListAsync(), c => Assert.Equal(A, c.TenantId));
        Assert.All(await db.Messages.IgnoreQueryFilters().ToListAsync(), m => Assert.Equal(A, m.TenantId));
        Assert.All(await db.ReadMarkers.IgnoreQueryFilters().ToListAsync(), r => Assert.Equal(A, r.TenantId));
        Assert.All(await db.SavedReplies.IgnoreQueryFilters().ToListAsync(), s => Assert.Equal(A, s.TenantId));
        Assert.Null(await db.Conversations.SingleOrDefaultAsync(c => c.Id == fixture.TenantB.ConversationId));
        Assert.Null(await db.Conversations.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.Id == fixture.TenantB.ConversationId));
    }

    [Fact]
    public async Task Writing_a_row_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<InboxDbContext>();

        db.SavedReplies.Add(SavedReply.Create(B, "intruder", "Intruder", "Hi", null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_AI_cannot_read_answer_or_hand_off_another_tenants_conversation()
    {
        var other = fixture.TenantB.ConversationId;
        int messagesBefore;
        await using (var tenantB = fixture.Scope(B))
        {
            messagesBefore = await tenantB.Get<InboxDbContext>().Messages.CountAsync(m => m.ConversationId == other);
        }

        await using (var scope = fixture.Scope(A))
        {
            var conversations = scope.Get<IAiConversations>();

            Assert.Null(await conversations.HistoryAsync(other, HistorySize, CancellationToken.None));
            Assert.Null(await conversations.ClaimAsync(other, HistorySize, CancellationToken.None));
            Assert.False(await conversations.ReplyAsync(other, ["Leaked answer"], CancellationToken.None));
            await conversations.HandOffAsync(other, "Leaked note", CancellationToken.None);
            Assert.Null(await scope.Get<IOrderConversations>().ContactIdAsync(other, CancellationToken.None));
        }

        await using var after = fixture.Scope(B);
        var db = after.Get<InboxDbContext>();
        Assert.Equal(messagesBefore, await db.Messages.CountAsync(m => m.ConversationId == other));
        Assert.Equal(Responder.Ai, (await db.Conversations.SingleAsync(c => c.Id == other)).Responder);
    }

    [Fact]
    public async Task Team_notes_never_appear_in_the_history_the_AI_gets()
    {
        await using var scope = fixture.Scope(A);

        var history = await scope.Get<IAiConversations>().HistoryAsync(fixture.TenantA.ConversationId, HistorySize, CancellationToken.None);

        Assert.NotNull(history);
        Assert.Contains(history.Messages, m => m.Author == "customer");
        Assert.DoesNotContain(history.Messages, m => m.Text.Contains(fixture.TenantA.NoteText, StringComparison.Ordinal));
        Assert.DoesNotContain(history.Messages, m => m.Text.Contains("Handed over", StringComparison.Ordinal));

        // The notes are there for the team, so the test above is not empty by accident.
        var internalTexts = await scope.Get<InboxDbContext>().Messages
            .Where(m => m.ConversationId == fixture.TenantA.ConversationId && m.Internal)
            .Select(m => m.Text)
            .ToListAsync();
        Assert.Contains(fixture.TenantA.NoteText, internalTexts);
    }

    [Theory]
    [InlineData("conversations")]
    [InlineData("messages")]
    [InlineData("read_markers")]
    [InlineData("saved_replies")]
    public Task Database_isolates_the_table(string table) =>
        TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "inbox", table);
}
