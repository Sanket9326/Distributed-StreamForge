using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StreamForge.Identity.Api.Infrastructure.Redis;

namespace StreamForge.Identity.Api.Services;

/// <summary>Configures fixed-window account and network request limits.</summary>
public sealed class AuthThrottleOptions
{
    /// <summary>Gets or sets the login limit per normalized email per fifteen minutes.</summary>
    public int LoginPerEmail { get; set; } = 10;
    /// <summary>Gets or sets the login limit per client IP per fifteen minutes.</summary>
    public int LoginPerIp { get; set; } = 60;
    /// <summary>Gets or sets the registration limit per client IP per hour.</summary>
    public int RegisterPerIp { get; set; } = 20;
}

/// <summary>Atomically counts attempts across replicas without retaining emails or IPs in keys.</summary>
public sealed class AuthThrottle(IConnectionMultiplexer redis, IOptions<AuthThrottleOptions> options, ILogger<LuaScriptExecutor>? logger = null)
{
    /// <summary>Checks both email and network login budgets.</summary>
    public async Task LoginAsync(string email, string ip, CancellationToken cancellationToken)
    {
        await CheckAsync("login-ip", ip, options.Value.LoginPerIp, 900, cancellationToken);
        await CheckAsync("login-email", email, options.Value.LoginPerEmail, 900, cancellationToken);
    }
    /// <summary>Checks the network registration budget.</summary>
    public Task RegisterAsync(string ip, CancellationToken cancellationToken) =>
        CheckAsync("register-ip", ip, options.Value.RegisterPerIp, 3600, cancellationToken);

    private async Task CheckAsync(string scope, string value, int limit, int seconds, CancellationToken cancellationToken)
    {
        var key = "streamforge:identity:limits:v1:" + scope + ":" +
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var retry = (int)await new LuaScriptExecutor(redis, logger).ExecuteAsync("throttle", [key], [seconds, limit], cancellationToken);
        if (retry > 0)
        {
            throw new AuthFailure(429, "rate_limited", "Too many attempts. Please try again later.", retry);
        }
    }
}
