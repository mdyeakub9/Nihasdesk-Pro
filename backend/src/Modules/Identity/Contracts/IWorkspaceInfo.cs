namespace Nihasdesk.Modules.Identity.Contracts;

/// <summary>Public facts about the current workspace, e.g. the shop name the AI introduces itself with.</summary>
public interface IWorkspaceInfo
{
    Task<string?> NameAsync(CancellationToken cancellationToken);
}
