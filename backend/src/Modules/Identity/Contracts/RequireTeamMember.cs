using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Contracts;

/// <summary>
/// Endpoint filter for work every role may do (inbox, contacts — docs/standards/permissions.md):
/// the signed-in user must still be a member of the current workspace (SEC-10).
/// </summary>
public sealed class RequireTeamMember : IEndpointFilter
{
    internal static readonly Error Forbidden = Error.Forbidden("team.forbidden", "Your role doesn't allow this.");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var team = context.HttpContext.RequestServices.GetRequiredService<ITeamDirectory>();
        if (await team.CurrentAsync(context.HttpContext.RequestAborted) is null)
        {
            return Forbidden.ToProblem();
        }

        return await next(context);
    }
}

/// <summary>
/// Endpoint filter for settings work: the current member's role must be one of
/// <c>owner</c> / <c>admin</c> / <c>member</c> listed (docs/standards/permissions.md, SEC-10).
/// </summary>
public sealed class RequireTeamRole(params string[] roles) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var team = context.HttpContext.RequestServices.GetRequiredService<ITeamDirectory>();
        var member = await team.CurrentAsync(context.HttpContext.RequestAborted);
        if (member is null || !roles.Contains(member.Role, StringComparer.Ordinal))
        {
            return RequireTeamMember.Forbidden.ToProblem();
        }

        return await next(context);
    }
}
