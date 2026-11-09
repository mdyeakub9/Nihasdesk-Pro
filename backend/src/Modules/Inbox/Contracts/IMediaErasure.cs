namespace Nihasdesk.Modules.Inbox.Contracts;

/// <summary>
/// Deletes customer media for erasure requests (SEC-45, ADR 0031 §8): the stored files and their rows.
/// Works in the current workspace only. The erasure flow of Issue #75 calls it; nothing does yet.
/// </summary>
public interface IMediaErasure
{
    /// <summary>All media of one contact's conversations; returns how many attachments were deleted.</summary>
    Task<int> EraseContactAsync(Guid contactId, CancellationToken cancellationToken);

    /// <summary>All media of the workspace, when it is closed; returns how many attachments were deleted.</summary>
    Task<int> EraseWorkspaceAsync(CancellationToken cancellationToken);
}
