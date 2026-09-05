using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace StreamForge.Identity.IntegrationTests;

public sealed class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.6-alpine")
        .WithDatabase("streamforge_identity").WithUsername("streamforge").WithPassword("integration-test-password").Build();
    private readonly IContainer redis = new ContainerBuilder("redis:8.2.1-alpine")
        .WithPortBinding(6379, true).WithCommand("redis-server", "--save", "", "--appendonly", "no", "--requirepass", "integration-test-password")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "-a", "integration-test-password", "ping")).Build();

    public string RedisConnection => $"{redis.Hostname}:{redis.GetMappedPublicPort(6379)},password=integration-test-password,abortConnect=false,asyncTimeout=500,connectTimeout=500";

    public async Task InitializeAsync() => await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
    public Task PauseRedisAsync() => redis.PauseAsync();
    public Task ResumeRedisAsync() => redis.UnpauseAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) =>
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:IdentityDatabase"] = postgres.GetConnectionString(),
            ["ConnectionStrings:Redis"] = RedisConnection,
            ["AuthThrottle:RegisterPerIp"] = "1000"
        }));

    public HttpClient Browser() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await redis.DisposeAsync();
        await postgres.DisposeAsync();
    }
}
