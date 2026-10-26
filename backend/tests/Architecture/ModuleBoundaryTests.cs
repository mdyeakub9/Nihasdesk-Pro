using Nihasdesk.Infrastructure.Modules;

namespace Nihasdesk.ArchitectureTests;

/// <summary>
/// Modules talk only through <c>Contracts/</c> (CLAUDE.md rule 2). Only the module entry point and
/// contract types may be public, so the compiler stops other modules from using internals.
/// </summary>
public class ModuleBoundaryTests
{
    [Theory]
    [MemberData(nameof(ModuleAssemblies.Names), MemberType = typeof(ModuleAssemblies))]
    public void Only_entry_point_and_contracts_are_public(string moduleAssembly)
    {
        var contractsNamespace = $"{moduleAssembly}.Contracts";

        var violations = ModuleAssemblies.ByName(moduleAssembly)
            .GetExportedTypes()
            .Where(t => !typeof(IModule).IsAssignableFrom(t))
            .Where(t => t.Namespace is null
                || (t.Namespace != contractsNamespace && !t.Namespace.StartsWith(contractsNamespace + ".", StringComparison.Ordinal)))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(violations.Count == 0, "Make these types internal or move them to Contracts: " + string.Join(", ", violations));
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies.Names), MemberType = typeof(ModuleAssemblies))]
    public void Each_module_has_exactly_one_entry_point(string moduleAssembly)
    {
        var entryPoints = ModuleAssemblies.ByName(moduleAssembly)
            .GetExportedTypes()
            .Where(t => typeof(IModule).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

        Assert.Single(entryPoints);
    }
}
