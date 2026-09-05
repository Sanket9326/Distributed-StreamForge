using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using StackExchange.Redis;

namespace StreamForge.Gateway.Api.Authentication;

/// <summary>Authenticates protected routes and validates antiforgery headers before proxying any request body.</summary>
public sealed class SessionMiddleware(RequestDelegate next)
{
    /// <summary>Builds verified user context and rejects unavailable or invalid sessions safely.</summary>
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        foreach (var header in context.Request.Headers.Keys.Where(x => x.StartsWith("X-StreamForge-", StringComparison.OrdinalIgnoreCase)).ToArray())
            context.Request.Headers.Remove(header);
        var path = context.Request.Path;
        var auth = path.StartsWithSegments("/api/auth");
        var protectedRoute = path.StartsWithSegments("/api/uploads") || path.Equals("/api/auth/me");
        // Only auth/protected routes touch Redis. Public playback survives a Redis outage.
        if (auth || protectedRoute)
        {
            context.Response.Headers.CacheControl = "no-store";
            var id = context.Request.Cookies[RedisSessionReader.CookieName];
            SessionRecord? session = null;
            try
            {
                if (!string.IsNullOrEmpty(id))
                    session = await context.RequestServices.GetRequiredService<ISessionReader>().ReadAsync(id, context.RequestAborted);
            }
            catch (Exception exception) when (exception is RedisException or TimeoutException)
            {
                await Problem(context, 503, "session_unavailable", "Authentication is temporarily unavailable.");
                return;
            }
            if (session is not null)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString("D"))], "RedisSession"));
                context.Request.Headers["X-StreamForge-User-Id"] = session.UserId.ToString("D");
            }
            else if (protectedRoute)
            {
                RedisSessionReader.ClearCookie(context.Response);
                await Problem(context, 401, "session_invalid", "Please log in.");
                return;
            }
            else if (id is not null) RedisSessionReader.ClearCookie(context.Response);
        }
        if (path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) &&
            !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
        {
            // Never let antiforgery fall back to reading a multipart body.
            if (string.IsNullOrEmpty(context.Request.Headers["X-XSRF-TOKEN"]))
            {
                await Problem(context, 403, "csrf_invalid", "Refresh the page and try again.");
                return;
            }
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                await Problem(context, 403, "csrf_invalid", "Refresh the page and try again.");
                return;
            }
        }
        if (auth) context.Request.Headers["X-StreamForge-Client-Ip"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await next(context);
    }

    private static Task Problem(HttpContext context, int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?>
        { ["code"] = code, ["correlationId"] = context.TraceIdentifier }).ExecuteAsync(context);
}
