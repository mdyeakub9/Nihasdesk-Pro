namespace Nihasdesk.Modules.Contacts.Contracts;

/// <summary>What the AI may know about a contact (ADR 0026 §4): no phone, email or address (SEC-37, rule 7).</summary>
public sealed record ContactProfile(string? FirstName, string? Language);

/// <summary>Narrow on purpose, so contact details can't reach the model through it.</summary>
public interface IContactProfile
{
    /// <summary>Null when the contact isn't in the current workspace.</summary>
    Task<ContactProfile?> GetAsync(Guid contactId, CancellationToken cancellationToken);
}
