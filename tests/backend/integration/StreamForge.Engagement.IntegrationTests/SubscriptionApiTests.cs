using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StreamForge.Engagement.Api.Models;

namespace StreamForge.Engagement.IntegrationTests;

public sealed class SubscriptionApiFactory(SubscriptionFixture fixture, bool accountExists = true) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:EngagementDatabase"] = fixture.DatabaseConnection,
            ["ConnectionStrings:Redis"] = fixture.RedisConnection,
            ["Kafka:BootstrapServers"] = fixture.Kafka.BootstrapServers
        }));
        builder.ConfigureTestServices(services => {
            // Deliberately pause consumers while exercising the real HTTP, Kafka and cache flow.
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SubscriptionFixture.Profiles(accountExists));
        });
    }
}

public sealed class SubscriptionApiTests(SubscriptionFixture fixture) : IClassFixture<SubscriptionFixture>
{
    [Fact]
    public async Task Endpoints_AuthenticateValidateAndExposeOnlyTheActingUsersLists()
    {
        using var app = new SubscriptionApiFactory(fixture);
        using var client = app.CreateClient();
        const string path = "/api/engagement/subscriptions";
        using var anonymous = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var user = Guid.NewGuid(); var creator = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-StreamForge-User-Id", user.ToString("D"));
        using var self = await client.PutAsync(path + "/" + user, null);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        using var accepted = await client.PutAsync(path + "/" + creator, null);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var mutation = (await accepted.Content.ReadFromJsonAsync<SubscriptionMutation>())!;
        Assert.Equal(user, mutation.SubscriberId);
        Assert.Equal(creator, mutation.CreatorId);
        Assert.True(mutation.IsActive);
        using var list = await client.GetAsync(path + "?limit=1");
        Assert.True(list.Headers.CacheControl!.NoStore);
        Assert.Equal(creator, Assert.Single((await list.Content.ReadFromJsonAsync<SubscriptionPage>())!.Items).UserId);
        var status = await client.GetFromJsonAsync<SubscriptionStatus[]>(path + "/status?creatorIds=" + creator);
        Assert.True(Assert.Single(status!).IsActive);
        using var tooMany = await client.GetAsync(path + "/status?" +
            string.Join('&', Enumerable.Range(0, 51).Select(_ => "creatorIds=" + Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        using var invalidLimit = await client.GetAsync(path + "?limit=51");
        Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);

        client.DefaultRequestHeaders.Remove("X-StreamForge-User-Id");
        client.DefaultRequestHeaders.Add("X-StreamForge-User-Id", creator.ToString("D"));
        var subscribers = await client.GetFromJsonAsync<SubscriptionPage>(path + "/subscribers");
        Assert.Equal(user, Assert.Single(subscribers!.Items).UserId);
        // Subscribe back creates a separate directed relationship.
        using var back = await client.PutAsync(path + "/" + user, null);
        Assert.Equal(HttpStatusCode.Accepted, back.StatusCode);
        using var remove = await client.DeleteAsync(path + "/subscribers/" + user);
        Assert.Equal(HttpStatusCode.Accepted, remove.StatusCode);
        var removed = (await remove.Content.ReadFromJsonAsync<SubscriptionMutation>())!;
        Assert.Equal(user, removed.SubscriberId);
        Assert.Equal(creator, removed.CreatorId);
        Assert.False(removed.IsActive);
        Assert.Empty((await client.GetFromJsonAsync<SubscriptionPage>(path + "/subscribers"))!.Items);
        Assert.True(Assert.Single((await client.GetFromJsonAsync<SubscriptionStatus[]>(path + "/status?creatorIds=" + user))!).IsActive);
        using var unsubscribe = await client.DeleteAsync(path + "/" + user);
        Assert.Equal(HttpStatusCode.Accepted, unsubscribe.StatusCode);
    }

    [Fact]
    public async Task Subscribe_RejectsUnknownIdentityAccountBeforePublication()
    {
        using var app = new SubscriptionApiFactory(fixture, false);
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-StreamForge-User-Id", Guid.NewGuid().ToString("D"));
        using var response = await client.PutAsync("/api/engagement/subscriptions/" + Guid.NewGuid(), null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

