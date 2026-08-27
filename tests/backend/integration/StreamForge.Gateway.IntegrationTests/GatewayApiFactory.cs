using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace StreamForge.Gateway.IntegrationTests;

public sealed class GatewayApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private WebApplication? downstream;
    private string? downstreamAddress;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        downstream = builder.Build();

        downstream.MapPost("/api/uploads", async context =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-Response-Status", out var status) &&
                status == "415")
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await Results.Problem(
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    title: "Downstream rejection",
                    detail: "Rejected by upload service.").ExecuteAsync(context);
                return;
            }

            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body);
            context.Response.StatusCode = StatusCodes.Status201Created;
            await Results.Json(new
            {
                receivedBytes = body.Length,
                correlationId = context.Request.Headers["X-Correlation-ID"].ToString()
            }).ExecuteAsync(context);
        });

        await downstream.StartAsync();
        downstreamAddress = downstream.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .Single();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (downstream is not null)
        {
            await downstream.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (downstreamAddress is null)
        {
            throw new InvalidOperationException("The downstream test server must be started first.");
        }

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:upload-cluster:Destinations:upload-service:Address"] =
                    $"{downstreamAddress}/"
            });
        });
    }
}
