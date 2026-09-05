using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using StackExchange.Redis;

namespace StreamForge.Identity.Api.Services;

/// <summary>Defines the version-one Redis session value consumed by Gateway.</summary>
public sealed record SessionRecord(Guid UserId, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

/// <summary>Pairs a newly minted browser secret with its server-side session.</summary>
public sealed record CreatedSession(string Id, SessionRecord Record);

/// <summary>Owns session creation, lookup and revocation without extending expiry.</summary>
public interface ISessionStore
{
    /// <summary>Creates a new session and revokes the session presented by this browser.</summary>
    /// <param name="userId">The verified account identifier.</param>
    /// <param name="previousId">The browser's previous opaque cookie, if any.</param>
    /// <param name="cancellationToken">Signals request cancellation.</param>
    /// <returns>The new secret and its absolute-lifetime record.</returns>
    Task<CreatedSession> CreateAsync(Guid userId, string? previousId, CancellationToken cancellationToken);
    /// <summary>Returns a live session or null for an invalid or expired identifier.</summary>
    /// <param name="id">The opaque browser cookie.</param>
    /// <param name="cancellationToken">Signals request cancellation.</param>
    /// <returns>A valid record, or null when the browser must sign in.</returns>
    Task<SessionRecord?> ReadAsync(string? id, CancellationToken cancellationToken);
    /// <summary>Revokes the supplied identifier; absent sessions are already revoked.</summary>
    /// <param name="id">The opaque cookie to revoke.</param>
    /// <param name="cancellationToken">Signals request cancellation.</param>
    /// <returns>A task completing only after Redis confirms deletion or absence.</returns>
    Task DeleteAsync(string? id, CancellationToken cancellationToken);
}

/// <summary>Stores only hashes of random browser secrets in the shared Redis instance.</summary>
public sealed class RedisSessionStore(IConnectionMultiplexer redis, TimeProvider clock) : ISessionStore
{
    /// <summary>Gets the public host-only cookie name.</summary>
    public const string CookieName = "__Host-streamforge-session";
    /// <summary>Gets the absolute session lifetime.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Validates canonical base64url secrets before deriving a bounded Redis key.</summary>
    public static string? Key(string? id)
    {
        if (id is null || id.Length != 43 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            return null;
        return "streamforge:identity:sessions:v1:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    }

    /// <inheritdoc />
    public async Task<CreatedSession> CreateAsync(Guid userId, string? previousId, CancellationToken cancellationToken)
    {
        var database = redis.GetDatabase();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var now = clock.GetUtcNow();
            var record = new SessionRecord(userId, now, now.Add(Lifetime));
            // Creation, TTL and previous-browser revocation happen in one Redis operation.
            const string script = "if redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2], 'NX') then " +
                "if KEYS[2] ~= KEYS[1] then redis.call('DEL', KEYS[2]) end return 1 else return 0 end";
            var key = Key(id)!;
            var result = await database.ScriptEvaluateAsync(script,
                [key, Key(previousId) ?? key], [JsonSerializer.Serialize(record, Json), (long)Lifetime.TotalMilliseconds])
                .WaitAsync(cancellationToken);
            if ((int)result == 1) return new CreatedSession(id, record);
        }
        throw new InvalidOperationException("Unable to allocate a unique session.");
    }

    /// <inheritdoc />
    public async Task<SessionRecord?> ReadAsync(string? id, CancellationToken cancellationToken)
    {
        var key = Key(id);
        if (key is null) return null;
        var value = await redis.GetDatabase().StringGetAsync(key).WaitAsync(cancellationToken);
        if (value.IsNullOrEmpty) return null;
        try
        {
            var session = JsonSerializer.Deserialize<SessionRecord>((string)value!, Json);
            var now = clock.GetUtcNow();
            return session is not null && session.UserId != Guid.Empty && session.CreatedAtUtc <= now &&
                session.ExpiresAtUtc > now && session.ExpiresAtUtc - session.CreatedAtUtc == Lifetime ? session : null;
        }
        catch (JsonException) { return null; }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string? id, CancellationToken cancellationToken)
    {
        if (Key(id) is { } key)
            await redis.GetDatabase().KeyDeleteAsync(key).WaitAsync(cancellationToken);
    }

    /// <summary>Creates matching cookie options for issuance and deletion.</summary>
    public static CookieOptions Cookie(DateTimeOffset? expiry = null) => new()
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/",
        Expires = expiry, MaxAge = expiry is null ? null : Lifetime, IsEssential = true
    };
}
