using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using StreamForge.Gateway.Api.Infrastructure.Redis;

namespace StreamForge.Gateway.Api.Authentication;

/// <summary>Describes the Identity-owned version-one session storage contract.</summary>
public sealed record SessionRecord(Guid UserId, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

/// <summary>Reads current session state without local caching or TTL renewal.</summary>
public interface ISessionReader
{
    /// <summary>Returns a valid session, null for invalid credentials, or throws for dependency failure.</summary>
    /// <param name="id">The browser's opaque cookie value.</param>
    /// <param name="cancellationToken">Signals request cancellation.</param>
    /// <returns>The current server-side session record, without renewing it.</returns>
    Task<SessionRecord?> ReadAsync(string? id, CancellationToken cancellationToken);
}

/// <summary>Validates the opaque cookie against the Identity-owned Redis namespace.</summary>
public sealed class RedisSessionReader(IConnectionMultiplexer redis, TimeProvider clock, ILogger<LuaScriptExecutor>? logger = null) : ISessionReader
{
    /// <summary>Gets the host-only session cookie name.</summary>
    public const string CookieName = "__Host-streamforge-session";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<SessionRecord?> ReadAsync(string? id, CancellationToken cancellationToken)
    {
        if (id is null || id.Length != 43 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) return null;
        var key = "streamforge:identity:sessions:v1:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
        var value = (string?)await new LuaScriptExecutor(redis, logger).ExecuteAsync("read-string", [key], [], cancellationToken);
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            var session = JsonSerializer.Deserialize<SessionRecord>((string)value!, Json);
            var now = clock.GetUtcNow();
            return session is not null && session.UserId != Guid.Empty && session.CreatedAtUtc <= now &&
                session.ExpiresAtUtc > now && session.ExpiresAtUtc - session.CreatedAtUtc == TimeSpan.FromHours(24) ? session : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Expires the browser cookie using its original security attributes.</summary>
    public static void ClearCookie(HttpResponse response) => response.Cookies.Delete(CookieName, new CookieOptions
    { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
}
