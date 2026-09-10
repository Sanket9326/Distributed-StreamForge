using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StreamForge.Search.Api.Services;

namespace StreamForge.Search.Api.Middleware;

public sealed class SearchExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<SearchExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not SearchUnavailableException)
        {
            return false;
        }

        logger.LogWarning(exception, "Search request failed because Elasticsearch is unavailable");
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Search is temporarily unavailable",
                Detail = "Video suggestions could not be loaded. Try again shortly."
            },
            Exception = exception
        });
    }
}
