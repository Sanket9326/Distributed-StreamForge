namespace StreamForge.Upload.Api.Middleware;

/// <summary>
/// Validates a caller-supplied correlation ID or creates one, then adds it to request logging and the response.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    /// <summary>Gets the HTTP header used to propagate correlation IDs.</summary>
    public const string HeaderName = "X-Correlation-ID";
    private const int MaximumCorrelationIdLength = 128;

    /// <summary>
    /// Applies correlation information before invoking the remaining request pipeline.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var suppliedCorrelationId = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsValid(suppliedCorrelationId)
            ? suppliedCorrelationId!
            : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Request.Headers[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await next(context);
        }
    }

    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumCorrelationIdLength &&
        value.All(character => character is >= '!' and <= '~');
}
