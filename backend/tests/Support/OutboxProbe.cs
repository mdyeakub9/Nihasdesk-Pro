using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Email;
using Nihasdesk.Infrastructure.Email.Outbox;
using Nihasdesk.Modules.Identity.Infrastructure;

namespace Nihasdesk.TestSupport;

/// <summary>Looks inside the Identity email outbox for tests (ADR 0013).</summary>
public sealed class OutboxProbe(NihasdeskApiFactory api)
{
    public async Task EnqueueAsync(EmailMessage message)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEmailOutbox>().EnqueueAsync(message, CancellationToken.None);
    }

    /// <summary>Queues inside a transaction that is then rolled back, like a failed request.</summary>
    public async Task EnqueueAndRollBackAsync(EmailMessage message)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IEmailOutbox>().EnqueueAsync(message, CancellationToken.None);
        await transaction.RollbackAsync();
    }

    public async Task<IReadOnlyList<OutboxEmail>> RowsAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        return await db.Set<OutboxEmail>().AsNoTracking().OrderBy(o => o.CreatedAt).ToListAsync();
    }

    /// <summary>Drains the outbox with two dispatchers at once, like two Workers instances.</summary>
    public Task DrainWithTwoDispatchersAsync()
    {
        var first = api.Services.GetRequiredService<EmailOutboxDispatcher<IdentityModuleDbContext>>();
        var second = ActivatorUtilities.CreateInstance<EmailOutboxDispatcher<IdentityModuleDbContext>>(api.Services);
        return Task.WhenAll(Task.Run(() => DrainAsync(first)), Task.Run(() => DrainAsync(second)));
    }

    private static async Task DrainAsync(EmailOutboxDispatcher<IdentityModuleDbContext> dispatcher)
    {
        while (await dispatcher.DispatchOnceAsync(CancellationToken.None) > 0)
        {
        }
    }
}
