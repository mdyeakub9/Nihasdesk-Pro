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
    private static readonly Error Forbidden = Error.Forbidden("team.forbidden", "Your role doesn't allow this.");

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
