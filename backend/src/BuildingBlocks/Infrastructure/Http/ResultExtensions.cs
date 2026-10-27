using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Infrastructure.Http;

public static class ResultExtensions
{
    /// <summary>
    /// Maps an expected failure to RFC 9457 Problem Details with its stable <c>code</c>
    /// (docs/standards/errors.md). The global handler adds the traceId.
    /// </summary>
    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Problem(
            statusCode: status,
            title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}

/// <summary>Runs the request's FluentValidation validator before the handler (docs/standards/api.md).</summary>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        var validator = context.HttpContext.RequestServices.GetService<IValidator<TRequest>>();
        if (request is null || validator is null)
        {
            return await next(context);
        }

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        return Results.ValidationProblem(
            result.ToDictionary(),
            extensions: new Dictionary<string, object?> { ["code"] = "validation.failed" });
    }
}
