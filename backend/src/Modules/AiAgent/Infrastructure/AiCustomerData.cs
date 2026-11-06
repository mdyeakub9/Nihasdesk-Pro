using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Contracts;

namespace Nihasdesk.Modules.AiAgent.Infrastructure;

internal sealed class AiCustomerData(AiDbContext db) : IAiCustomerData
{
    public Task EraseContactAsync(Guid contactId, CancellationToken cancellationToken) =>
        db.Memory.Where(m => m.ContactId == contactId).ExecuteDeleteAsync(cancellationToken);

    public Task EraseWorkspaceAsync(CancellationToken cancellationToken) =>
        db.Memory.ExecuteDeleteAsync(cancellationToken);
}
