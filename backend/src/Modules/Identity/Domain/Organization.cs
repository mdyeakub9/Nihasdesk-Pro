using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Domain;

/// <summary>
/// Groups workspaces for billing and, later, agency access (ADR 0003). Not tenant-owned itself:
/// it is reached only through a workspace and stays inside the Identity module.
/// </summary>
internal sealed class Organization : Entity
{
    public const int NameMaxLength = 200;

    private Organization()
    {
        Name = string.Empty;
    }

    private Organization(string name)
    {
        Name = name;
    }

    public string Name { get; private set; }

    public static Organization Create(string name) => new(WorkspaceNames.Validate(name, NameMaxLength));
}
