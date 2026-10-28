namespace Nihasdesk.Modules.Identity.Contracts;

/// <summary>
/// The append-only audit log (SEC-47) for other modules, e.g. <c>channel.connected</c>. Records the
/// signed-in user, the current workspace and the client IP. Actions are short codes, never personal data.
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(string action, CancellationToken cancellationToken);
}
