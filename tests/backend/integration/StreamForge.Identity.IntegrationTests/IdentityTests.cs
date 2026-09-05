extern alias Gateway;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using StreamForge.Identity.Api.Data;
using StreamForge.Identity.Api.Models;
using StreamForge.Identity.Api.Services;
using GatewayReader = Gateway::StreamForge.Gateway.Api.Authentication.RedisSessionReader;

namespace StreamForge.Identity.IntegrationTests;

public sealed class IdentityTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Password = "a sufficiently long password";
    private static RegisterRequest Registration(string? name = null) => new(name ?? "user" + Guid.NewGuid().ToString("N"),
        Guid.NewGuid().ToString("N") + "@example.test", Password, null, null);

    [Fact]
    public async Task Register_PersistsHashedAccountAndIssuesSecureSessionReadableByGateway()
    {
        using var browser = factory.Browser();
        var request = Registration() with { Dob = new DateOnly(2000, 1, 2), Address = "Optional address" };
        using var response = await browser.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(RedisSessionStore.CookieName));
        foreach (var flag in new[] { "secure", "httponly", "samesite=strict", "path=/", "max-age=86400" })
            Assert.Contains(flag, cookie.ToLowerInvariant());
        Assert.DoesNotContain("domain=", cookie.ToLowerInvariant());
        var id = cookie.Split(';')[0].Split('=', 2)[1];
        Assert.Equal(43, id.Length);
        Assert.DoesNotContain(id, await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await database.Users.SingleAsync(x => x.Id == auth.User.Id);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.Equal(request.Dob, user.Dob);
        Assert.Equal(request.Address, user.Address);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var stored = await new GatewayReader(redis, TimeProvider.System).ReadAsync(id, default);
        Assert.Equal(user.Id, stored!.UserId);
        Assert.Equal(auth.ExpiresAtUtc, stored.ExpiresAtUtc);
        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(RedisSessionStore.Key(id)!);
        Assert.InRange(ttl!.Value.TotalSeconds, 86300, 86400);
        using var me = await browser.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains("no-store", me.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Login_CaseInsensitiveAndWrongOrUnknownCredentialsAreGeneric()
    {
        using var browser = factory.Browser();
        var request = Registration();
        using var registration = await browser.PostAsJsonAsync("/api/auth/register", request);
        registration.EnsureSuccessStatusCode();
        using var login = await browser.PostAsJsonAsync("/api/auth/login", new LoginRequest(request.Email.ToUpperInvariant(), Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var wrong = await browser.PostAsJsonAsync("/api/auth/login", new LoginRequest(request.Email, "an incorrect password"));
        using var unknown = await browser.PostAsJsonAsync("/api/auth/login", new LoginRequest("missing@example.test", Password));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString(),
            (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_ConcurrentNormalizedDuplicates_CreateExactlyOneAccount()
    {
        using var first = factory.Browser(); using var second = factory.Browser();
        var request = Registration();
        var responses = await Task.WhenAll(first.PostAsJsonAsync("/api/auth/register", request),
            second.PostAsJsonAsync("/api/auth/register", request with { Username = request.Username.ToUpperInvariant(), Email = request.Email.ToUpperInvariant() }));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Register_FutureBirthDateAndShortPassword_ReturnValidationErrors()
    {
        using var browser = factory.Browser();
        using var future = await browser.PostAsJsonAsync("/api/auth/register", Registration() with { Dob = new DateOnly(9999, 1, 1) });
        using var shortPassword = await browser.PostAsJsonAsync("/api/auth/register", Registration() with { Password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
    }

    [Fact]
    public async Task Sessions_DevicesRotateIndependentlyAndLogoutIsIdempotent()
    {
        var store = factory.Services.GetRequiredService<ISessionStore>();
        var user = Guid.NewGuid();
        var first = await store.CreateAsync(user, null, default);
        var other = await store.CreateAsync(user, null, default);
        var rotated = await store.CreateAsync(user, first.Id, default);
        Assert.NotEqual(first.Id, rotated.Id);
        Assert.Null(await store.ReadAsync(first.Id, default));
        Assert.NotNull(await store.ReadAsync(other.Id, default));
        await store.DeleteAsync(rotated.Id, default);
        await store.DeleteAsync(rotated.Id, default);
        Assert.Null(await store.ReadAsync(rotated.Id, default));
        Assert.NotNull(await store.ReadAsync(other.Id, default));
        using var browser = factory.Browser();
        using var response = await browser.PostAsJsonAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Sessions_ReadDoesNotExtendTtlAndRejectsAbsoluteExpiryOrMalformedData()
    {
        var store = factory.Services.GetRequiredService<ISessionStore>();
        var created = await store.CreateAsync(Guid.NewGuid(), null, default);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var database = redis.GetDatabase();
        var key = RedisSessionStore.Key(created.Id)!;
        await database.KeyExpireAsync(key, TimeSpan.FromSeconds(30));
        Assert.NotNull(await store.ReadAsync(created.Id, default));
        Assert.InRange((await database.KeyTimeToLiveAsync(key))!.Value.TotalSeconds, 1, 30);
        var clock = new FixedClock(created.Record.ExpiresAtUtc);
        Assert.Null(await new RedisSessionStore(redis, clock).ReadAsync(created.Id, default));
        Assert.Null(await new GatewayReader(redis, clock).ReadAsync(created.Id, default));
        await database.StringSetAsync(key, "broken-json", TimeSpan.FromMinutes(1));
        Assert.Null(await store.ReadAsync(created.Id, default));
        Assert.Null(await new GatewayReader(redis, TimeProvider.System).ReadAsync(created.Id, default));
        await database.KeyDeleteAsync(key);
    }

    [Fact]
    public async Task Register_SessionCreationFails_AccountRemainsAndNoCookieIsIssued()
    {
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<ISessionStore>(); services.AddSingleton<ISessionStore, FailingSessionStore>(); }));
        using var browser = failing.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var request = Registration();
        using var response = await browser.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("account_created_session_unavailable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AnyAsync(x => x.NormalizedEmail == AccountService.Normalize(request.Email)));
    }

    [Fact]
    public async Task Logout_RedisUnavailable_DoesNotAcknowledgeRevocationOrDeleteCookie()
    {
        using var browser = factory.Browser();
        using var registration = await browser.PostAsJsonAsync("/api/auth/register", Registration());
        registration.EnsureSuccessStatusCode();
        await factory.PauseRedisAsync();
        try
        {
            using var response = await browser.PostAsJsonAsync("/api/auth/logout", new { });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
        finally { await factory.ResumeRedisAsync(); }
    }

    [Fact]
    public async Task Login_ExceedsEmailBudget_Returns429WithRetryAfter()
    {
        using var browser = factory.Browser();
        var request = new LoginRequest(Guid.NewGuid().ToString("N") + "@example.test", Password);
        for (var i = 0; i < 10; i++)
        {
            using var response = await browser.PostAsJsonAsync("/api/auth/login", request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var blocked = await browser.PostAsJsonAsync("/api/auth/login", request);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class FailingSessionStore : ISessionStore
    {
        public Task<CreatedSession> CreateAsync(Guid userId, string? previousId, CancellationToken cancellationToken) => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test outage");
        public Task<SessionRecord?> ReadAsync(string? id, CancellationToken cancellationToken) => Task.FromResult<SessionRecord?>(null);
        public Task DeleteAsync(string? id, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
