namespace Nihasdesk.Modules.Identity.Domain;

/// <summary>Workspace roles (ADR 0011). Stored as lower-case text.</summary>
internal enum Role
{
    Owner,
    Admin,
    Member,
}
