using System.Data.Common;
using Confluent.Kafka;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StreamForge.Engagement.Api.Services;

namespace StreamForge.Engagement.Api.Middleware;

public sealed class EngagementExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<EngagementExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) return false;
        var (status, title, detail, level) = Map(exception);
        if (level is not LogLevel.None)
            logger.Log(level, exception, "Engagement request failed with status {StatusCode}", status);
        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status, Title = title, Detail = detail, Instance = context.Request.Path,
                Extensions = { ["correlationId"] = context.TraceIdentifier }
            }
        });
    }

    private static (int, string, string, LogLevel) Map(Exception exception)
    {
        if (exception is EngagementRequestException request)
            return (request.StatusCode, request.Title, request.Message, LogLevel.None);
        if (exception is KafkaException or HttpRequestException || IsDatabaseFailure(exception))
            return (503, "Engagement temporarily unavailable", "The interaction could not be completed. Retry later.", LogLevel.Error);
        return (500, "Engagement request failed", "The interaction could not be completed.", LogLevel.Error);
    }

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is DbUpdateException or NpgsqlException or DbException ||
        exception.InnerException is not null && IsDatabaseFailure(exception.InnerException);
}
