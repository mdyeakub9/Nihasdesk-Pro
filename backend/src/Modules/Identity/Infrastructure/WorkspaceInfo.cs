using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Identity.Contracts;

namespace Nihasdesk.Modules.Identity.Infrastructure;

/// <summary>Reads through the tenant-scoped context: only the current workspace exists here.</summary>
internal sealed class WorkspaceInfo(IdentityModuleDbContext db) : IWorkspaceInfo
{
    public Task<string?> NameAsync(CancellationToken cancellationToken) =>
        db.Workspaces.Select(w => (string?)w.Name).SingleOrDefaultAsync(cancellationToken);
}
