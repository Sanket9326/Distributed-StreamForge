using Microsoft.AspNetCore.Mvc;
using StreamForge.Identity.Api.Models;
using StreamForge.Identity.Api.Services;

namespace StreamForge.Identity.Api.Controllers;

/// <summary>Exposes browser account operations through the private Gateway route.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(AccountService accounts, ISessionStore sessions) : ControllerBase
{
    /// <summary>Creates an account and sets its first secure browser session cookie.</summary>
    /// <param name="request">The validated registration fields.</param>
    /// <param name="cancellationToken">Signals browser disconnection or cancellation.</param>
    /// <returns>A 201 response with safe account state.</returns>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.RegisterAsync(request, ClientIp(), SessionId(), cancellationToken);
        SetSession(result.Session);
        return StatusCode(201, new AuthResponse(new(result.User.Id, result.User.Username, result.User.Email), result.Session.Record.ExpiresAtUtc));
    }

    /// <summary>Authenticates email credentials and replaces this browser's session.</summary>
    /// <param name="request">The validated credential input.</param>
    /// <param name="cancellationToken">Signals browser disconnection or cancellation.</param>
    /// <returns>Safe account state and expiry; the secret travels only in Set-Cookie.</returns>
    [HttpPost("login")]
    public async Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.LoginAsync(request, ClientIp(), SessionId(), cancellationToken);
        SetSession(result.Session);
        return new(new(result.User.Id, result.User.Username, result.User.Email), result.Session.Record.ExpiresAtUtc);
    }

    /// <summary>Returns account state for a live session.</summary>
    /// <param name="cancellationToken">Signals browser disconnection or cancellation.</param>
    /// <returns>The signed-in account and unchanged session expiry.</returns>
    [HttpGet("me")]
    public Task<AuthResponse> Me(CancellationToken cancellationToken) => accounts.MeAsync(SessionId(), cancellationToken);

    /// <summary>Revokes the current browser session before acknowledging logout.</summary>
    /// <param name="cancellationToken">Signals browser disconnection or cancellation.</param>
    /// <returns>A 204 response after confirmed revocation.</returns>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sessions.DeleteAsync(SessionId(), cancellationToken);
        Response.Cookies.Delete(RedisSessionStore.CookieName, RedisSessionStore.Cookie());
        Response.Cookies.Delete("XSRF-TOKEN", new CookieOptions { Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
        Response.Cookies.Delete("__Host-streamforge-antiforgery", RedisSessionStore.Cookie());
        return NoContent();
    }

    private string? SessionId() => Request.Cookies[RedisSessionStore.CookieName];
    // Gateway overwrites this header; Identity has no public listener in the deployed network.
    private string ClientIp() => Request.Headers["X-StreamForge-Client-Ip"].FirstOrDefault()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private void SetSession(CreatedSession session) => Response.Cookies.Append(
        RedisSessionStore.CookieName, session.Id, RedisSessionStore.Cookie(session.Record.ExpiresAtUtc));
}
