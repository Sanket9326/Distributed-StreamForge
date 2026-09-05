using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;
using StreamForge.Identity.Api.Data;
using StreamForge.Identity.Api.Models;

namespace StreamForge.Identity.Api.Services;

/// <summary>Coordinates durable account creation, password verification and browser sessions.</summary>
public sealed class AccountService(IdentityDbContext database, PasswordService passwords,
    ISessionStore sessions, AuthThrottle throttle, TimeProvider clock)
{
    /// <summary>Normalizes lookup values identically for account queries and rate-limit keys.</summary>
    public static string Normalize(string value) => value.Trim().Normalize().ToUpperInvariant();

    /// <summary>Persists a new account before creating its first browser session.</summary>
    /// <param name="request">Validated credentials and optional profile input.</param>
    /// <param name="clientIp">The network address supplied by the trusted Gateway.</param>
    /// <param name="previousId">The browser's previous opaque cookie.</param>
    /// <param name="cancellationToken">Signals request cancellation.</param>
    /// <returns>The persisted user and newly allocated session.</returns>
    public async Task<(User User, CreatedSession Session)> RegisterAsync(RegisterRequest request,
        string clientIp, string? previousId, CancellationToken cancellationToken)
    {
        await throttle.RegisterAsync(clientIp, cancellationToken);
        if (request.Dob > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            throw new AuthFailure(400, "invalid_dob", "Date of birth cannot be in the future.");
        var user = new User
        {
            Id = Guid.NewGuid(), Username = request.Username, Email = request.Email.Trim(),
            NormalizedUsername = Normalize(request.Username), NormalizedEmail = Normalize(request.Email),
            CreatedAtUtc = clock.GetUtcNow(), Dob = request.Dob,
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim()
        };
        user.PasswordHash = passwords.Hash(user, request.Password);
        database.Users.Add(user);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new AuthFailure(409, "account_exists", "That username or email is already registered.");
        }
        try { return (user, await sessions.CreateAsync(user.Id, previousId, cancellationToken)); }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            throw new AuthFailure(503, "account_created_session_unavailable",
                "Your account was created, but sign-in is temporarily unavailable. Please log in when the service recovers.");
        }
    }

    /// <summary>Verifies credentials and rotates only the session presented by this browser.</summary>
    /// <param name="request">Validated email and unmodified password.</param>
    /// <param name="clientIp">The trusted client address for throttling.</param>
    /// <param name="previousId">The session to replace after authentication succeeds.</param>
    /// <param name="cancellationToken">Signals request cancellation.</param>
    /// <returns>The verified account and its fresh session.</returns>
    public async Task<(User User, CreatedSession Session)> LoginAsync(LoginRequest request,
        string clientIp, string? previousId, CancellationToken cancellationToken)
    {
        var email = Normalize(request.Email);
        await throttle.LoginAsync(email, clientIp, cancellationToken);
        var user = await database.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == email, cancellationToken);
        var verification = passwords.Verify(user, request.Password);
        if (verification == PasswordVerificationResult.Failed || user is null)
            throw new AuthFailure(401, "invalid_credentials", "Email or password is incorrect.");
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwords.Hash(user, request.Password);
            await database.SaveChangesAsync(cancellationToken);
        }
        return (user, await sessions.CreateAsync(user.Id, previousId, cancellationToken));
    }

    /// <summary>Loads the browser's live account without refreshing its session.</summary>
    /// <param name="id">The current opaque session cookie.</param>
    /// <param name="cancellationToken">Signals request cancellation.</param>
    /// <returns>Safe account fields and the original absolute expiry.</returns>
    public async Task<AuthResponse> MeAsync(string? id, CancellationToken cancellationToken)
    {
        var session = await sessions.ReadAsync(id, cancellationToken);
        var user = session is null ? null : await database.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == session.UserId, cancellationToken);
        if (user is null) throw new AuthFailure(401, "session_invalid", "Please log in.");
        return new AuthResponse(new(user.Id, user.Username, user.Email), session!.ExpiresAtUtc);
    }
}
