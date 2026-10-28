using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Npgsql;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>SEC-47: the audit log is append-only for the app — the database refuses changes and deletes.</summary>
[Collection(PostgresTests.Name)]
public sealed class AuditLogAppendOnlyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task App_role_can_append_and_read_but_not_change_or_delete_audit_events()
    {
        var id = Guid.CreateVersion7();
        await using var scope = fixture.Scope(tenantId: null);
        scope.Db.AuditEvents.Add(new AuditEvent { Id = id, OccurredAt = DateTimeOffset.UtcNow, Action = "test.append" });
        await scope.Db.SaveChangesAsync();
        Assert.True(await scope.Db.AuditEvents.AnyAsync(a => a.Id == id));

        var update = await Assert.ThrowsAsync<PostgresException>(() => scope.Db.Database.ExecuteSqlAsync(
            $"UPDATE identity.audit_events SET action = 'test.tampered' WHERE id = {id}"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => scope.Db.Database.ExecuteSqlAsync(
            $"DELETE FROM identity.audit_events WHERE id = {id}"));
        var truncate = await Assert.ThrowsAsync<PostgresException>(() => scope.Db.Database.ExecuteSqlAsync(
            $"TRUNCATE identity.audit_events"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, update.SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, delete.SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, truncate.SqlState);
    }
}
