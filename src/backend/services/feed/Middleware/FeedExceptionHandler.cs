using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Minio.Exceptions;
using Npgsql;
using StreamForge.Feed.Api.Services;

namespace StreamForge.Feed.Api.Middleware;

public sealed class FeedExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<FeedExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var error = Map(exception);
        if (error.LogLevel is not LogLevel.None)
        {
            logger.Log(error.LogLevel, exception, "Feed request failed with status {StatusCode}", error.Status);
        }

        httpContext.Response.StatusCode = error.Status;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = error.Status,
                Title = error.Title,
                Detail = error.Detail,
                Instance = httpContext.Request.Path,
                Extensions = { ["correlationId"] = httpContext.TraceIdentifier }
            }
        });
        return true;
    }

    private static Error Map(Exception exception)
    {
        if (exception is FeedRequestException request)
        {
            return new Error(request.StatusCode, request.Title, request.Message, LogLevel.None);
        }

        if (exception is MinioException or HttpRequestException || IsDatabaseFailure(exception))
        {
            return new Error(
                StatusCodes.Status503ServiceUnavailable,
                "Feed temporarily unavailable",
                "The video feed could not be loaded. Retry later.",
                LogLevel.Error);
        }

        return new Error(
            StatusCodes.Status500InternalServerError,
            "Feed request failed",
            "The video feed request could not be completed.",
            LogLevel.Error);
    }

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is DbUpdateException or NpgsqlException or DbException or RetryLimitExceededException ||
        exception.InnerException is not null && IsDatabaseFailure(exception.InnerException);

    private sealed record Error(int Status, string Title, string Detail, LogLevel LogLevel);
}
