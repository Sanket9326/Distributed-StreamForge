using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using StackExchange.Redis;
using StreamForge.Identity.Api.Services;

namespace StreamForge.Identity.Api.Middleware;

/// <summary>Converts expected failures to safe Problem Details without logging sensitive payloads.</summary>
public sealed class IdentityExceptionHandler(ILogger<IdentityExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return false;
        var failure = exception as AuthFailure;
        var unavailable = exception is RedisException or NpgsqlException or TimeoutException || exception.InnerException is NpgsqlException;
        var status = failure?.Status ?? (unavailable ? 503 : 500);
        if (failure is null)
            logger.LogError("Identity request failed with {FailureType}; correlation {CorrelationId}", exception.GetType().Name, context.TraceIdentifier);
        if (status == 401 && failure?.Code == "session_invalid")
            context.Response.Cookies.Delete(RedisSessionStore.CookieName, RedisSessionStore.Cookie());
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        if (failure?.RetryAfterSeconds is { } retry)
            context.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status, Title = failure?.Message ?? (unavailable ? "Authentication is temporarily unavailable." : "An unexpected error occurred."),
            Extensions = { ["code"] = failure?.Code ?? (unavailable ? "identity_unavailable" : "internal_error"), ["correlationId"] = context.TraceIdentifier }
        }, cancellationToken: cancellationToken);
        return true;
    }
}
