using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ReadNest.Application.Common;
using ReadNest.Domain.Exceptions;

namespace ReadNest.Web.Api;

/// <summary>
/// Converts exceptions from API endpoints into RFC 7807 problem responses. Expected application failures keep
/// their user-safe message; anything else becomes a generic 500 without internal details.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
        {
            return false;
        }

        var problem = exception switch
        {
            RequestValidationException validation => new ValidationProblemDetails(validation.Errors.ToDictionary(kv => kv.Key, kv => kv.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            DomainException domain => new ValidationProblemDetails(new Dictionary<string, string[]> { [domain.Field ?? "request"] = [domain.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = domain.Message,
            },
            BadHttpRequestException badRequest => new ProblemDetails { Status = badRequest.StatusCode, Title = "The request was malformed." },
            UnauthorizedAccessException => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Authentication is required." },
            NotFoundException notFound => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = notFound.Message },
            ConflictException conflict => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = conflict.Message },
            ServiceUnavailableException unavailable => new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable, Title = unavailable.Message },
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => null,
            _ => new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "An unexpected error occurred." },
        };

        if (problem is null)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
