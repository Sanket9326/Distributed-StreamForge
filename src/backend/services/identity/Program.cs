using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using StreamForge.Identity.Api.Data;
using StreamForge.Identity.Api.Middleware;
using StreamForge.Identity.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(x => x.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
    new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState)
    { Extensions = { ["code"] = "validation_failed" } }));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<IdentityExceptionHandler>();
builder.Services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("IdentityDatabase"), x => x.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(
    builder.Configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Redis configuration is required.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<PasswordHasherOptions>(x => { x.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3; x.IterationCount = 600_000; });
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<ISessionStore, RedisSessionStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddOptions<AuthThrottleOptions>().BindConfiguration("AuthThrottle")
    .Validate(x => x.LoginPerEmail > 0 && x.LoginPerIp > 0 && x.RegisterPerIp > 0, "Auth limits must be positive.").ValidateOnStart();
builder.Services.AddSingleton<AuthThrottle>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddHealthChecks().AddCheck<IdentityReadiness>("identity-dependencies", tags: ["ready"]);
builder.Services.AddHostedService<IdentityInitializer>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    var correlation = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (correlation is { Length: > 0 and <= 128 } && correlation.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        context.TraceIdentifier = correlation;
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseExceptionHandler();
app.MapControllers();
app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = registration => registration.Tags.Contains("ready") });
app.Run();

/// <summary>Exposes the application entry point for integration hosting.</summary>
public partial class Program;

/// <summary>Applies Identity-owned committed migrations before accepting requests.</summary>
public sealed class IdentityInitializer(IServiceScopeFactory scopes) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(cancellationToken);
    }
    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Checks account persistence and session availability independently of liveness.</summary>
public sealed class IdentityReadiness(IServiceScopeFactory scopes, IConnectionMultiplexer redis) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopes.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Account database unavailable.");
            await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return HealthCheckResult.Unhealthy("Identity dependencies unavailable."); }
    }
}
