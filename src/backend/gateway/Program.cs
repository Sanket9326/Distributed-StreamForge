using StreamForge.Gateway.Api.Middleware;
using StreamForge.Gateway.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using StackExchange.Redis;
using Yarp.ReverseProxy.Transforms;

const long maximumRequestBodyBytes = 1_074_790_400;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maximumRequestBodyBytes);

builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(
    builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false,connectTimeout=2000,asyncTimeout=2000"));
builder.Services.AddSingleton<ISessionReader, RedisSessionReader>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "__Host-streamforge-antiforgery";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
var protection = builder.Services.AddDataProtection().SetApplicationName("StreamForge.Gateway");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var address in builder.Configuration.GetSection("TrustedProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
});
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builder => builder.AddRequestTransform(context =>
    {
        if (!context.HttpContext.Request.Path.StartsWithSegments("/api/auth"))
            context.ProxyRequest.Headers.Remove("Cookie");
        return ValueTask.CompletedTask;
    }));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SessionMiddleware>();

app.MapHealthChecks("/health");
app.MapGet("/health/ready", async (IConnectionMultiplexer redis, CancellationToken cancellationToken) =>
{
    try { await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken); return Results.Ok(); }
    catch (RedisException) { return Results.StatusCode(503); }
});
app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
    { Secure = true, HttpOnly = false, SameSite = SameSiteMode.Strict, Path = "/" });
    return Results.NoContent();
});
app.MapReverseProxy();

app.Run();

public partial class Program;
