using NetArchTest.Rules;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.ArchitectureTests;

public class LayerTests
{
    [Fact]
    public void SharedKernel_does_not_depend_on_infrastructure_modules_or_frameworks()
    {
        var result = Types.InAssembly(typeof(Entity).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Nihasdesk.Infrastructure",
                "Nihasdesk.Modules",
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_modules()
    {
        var result = Types.InAssembly(typeof(IModule).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Nihasdesk.Modules")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies.Names), MemberType = typeof(ModuleAssemblies))]
    public void Module_domain_does_not_depend_on_infrastructure_or_frameworks(string moduleAssembly)
    {
        var result = Types.InAssembly(ModuleAssemblies.ByName(moduleAssembly))
            .That()
            .ResideInNamespace($"{moduleAssembly}.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Nihasdesk.Infrastructure",
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    internal static string Describe(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
