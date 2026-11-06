namespace Nihasdesk.Modules.AiAgent.Contracts;

/// <summary>
/// What the AI keeps about customers, for the erasure flows (SEC-45). The customer profile is computed
/// and stored nowhere; the running summary is what goes. Called by those flows when they are built.
/// </summary>
public interface IAiCustomerData
{
    /// <summary>Removes the summaries of one contact in the current workspace.</summary>
    Task EraseContactAsync(Guid contactId, CancellationToken cancellationToken);

    /// <summary>Removes all summaries of the current workspace, on account closure.</summary>
    Task EraseWorkspaceAsync(CancellationToken cancellationToken);
}
