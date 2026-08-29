using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.Api.Middleware;

/// <summary>
/// Converts ingestion exceptions into stable Problem Details responses with correlation IDs.
/// </summary>
public sealed class UploadExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<UploadExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
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
        Log(exception, error);

        httpContext.Response.StatusCode = error.StatusCode;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = error.StatusCode,
                Title = error.Title,
                Detail = error.Detail,
                Instance = httpContext.Request.Path,
                Extensions =
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                }
            }
        });

        return true;
    }

    private static UploadError Map(Exception exception)
    {
        if (exception is UploadRequestException requestException)
        {
            return new UploadError(
                requestException.StatusCode,
                requestException.Title,
                requestException.Message,
                LogLevel.None);
        }

        if (exception is ObjectStorageException)
        {
            return new UploadError(
                StatusCodes.Status503ServiceUnavailable,
                "Object storage unavailable",
                "The video could not be stored. Retry the upload later.",
                LogLevel.Error);
        }

        if (IsDatabaseFailure(exception))
        {
            return new UploadError(
                StatusCodes.Status503ServiceUnavailable,
                "Metadata storage unavailable",
                "The video metadata could not be stored. Retry the upload later.",
                LogLevel.Error);
        }

        if (exception is InvalidDataException)
        {
            return new UploadError(
                StatusCodes.Status400BadRequest,
                "Malformed multipart request",
                "The multipart request could not be read.",
                LogLevel.Warning);
        }

        return new UploadError(
            StatusCodes.Status500InternalServerError,
            "Upload failed",
            "The upload could not be completed. Retry the upload later.",
            LogLevel.Error);
    }

    private void Log(Exception exception, UploadError error)
    {
        if (error.LogLevel == LogLevel.None)
        {
            return;
        }

        logger.Log(
            error.LogLevel,
            exception,
            "Upload request failed with status {StatusCode}",
            error.StatusCode);
    }

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is DbUpdateException or NpgsqlException or DbException or RetryLimitExceededException ||
        exception.InnerException is not null && IsDatabaseFailure(exception.InnerException);

    private sealed record UploadError(
        int StatusCode,
        string Title,
        string Detail,
        LogLevel LogLevel);
}
