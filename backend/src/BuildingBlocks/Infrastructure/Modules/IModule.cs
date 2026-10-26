using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Nihasdesk.Infrastructure.Modules;

/// <summary>
/// Entry point of a module. The host calls it to register services and map endpoints;
/// everything else in a module stays internal (enforced by architecture tests).
/// </summary>
public interface IModule
{
    /// <summary>Module name; also its database schema (e.g. <c>inbox</c>).</summary>
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
