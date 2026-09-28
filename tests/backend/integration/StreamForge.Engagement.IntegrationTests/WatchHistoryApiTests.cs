using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Services;

namespace StreamForge.Engagement.IntegrationTests;

public sealed class WatchHistoryApiTests(SubscriptionFixture fixture) : IClassFixture<SubscriptionFixture>
{
    [Fact]
    public async Task Endpoints_AuthenticateValidateAndAcknowledgeBeforeProjection()
    {
        using var app = new SubscriptionApiFactory(fixture);
        using var client = app.CreateClient();
        const string path = "/api/engagement/watch-history";
        var user = Guid.NewGuid(); var video = Guid.NewGuid();
        await using var db = await fixture.Contexts.CreateDbContextAsync();
        db.Videos.Add(new KnownVideo { VideoId = video, AvailableAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + "/" + video)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(path + "/" + video, new SaveWatchProgress(0, 1000, false))).StatusCode);
        client.DefaultRequestHeaders.Add("X-StreamForge-User-Id", user.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(path + "/" + video)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "?limit=51")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path + "/" + video, new { durationMs = 1000 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path + "/" + video, new SaveWatchProgress(1001, 1000, false))).StatusCode);
        var accepted = await client.PutAsJsonAsync(path + "/" + video, new SaveWatchProgress(400, 1000, false));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.False((await accepted.Content.ReadFromJsonAsync<WatchHistoryMutation>())!.CachePending);
        Assert.False(await db.WatchHistory.AnyAsync(x => x.UserId == user));
        var response = await client.GetAsync(path);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var entry = Assert.Single((await response.Content.ReadFromJsonAsync<WatchHistoryPage>())!.Items);
        Assert.Equal(video, entry.VideoId);
        Assert.Equal(400, entry.PositionMs);
        client.DefaultRequestHeaders.Remove("X-StreamForge-User-Id");
        client.DefaultRequestHeaders.Add("X-StreamForge-User-Id", Guid.NewGuid().ToString());
        Assert.Empty((await client.GetFromJsonAsync<WatchHistoryPage>(path))!.Items);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(path + "/" + video)).StatusCode);
    }

    [Fact]
    public async Task CacheFailureAfterAcceptance_ReturnsPendingAndAllowsDurableFallback()
    {
        using var app = new SubscriptionApiFactory(fixture);
        using var client = app.CreateClient();
        var user = Guid.NewGuid(); var video = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-StreamForge-User-Id", user.ToString());
        await using var db = await fixture.Contexts.CreateDbContextAsync();
        db.Videos.Add(new KnownVideo { VideoId = video, AvailableAtUtc = DateTimeOffset.UtcNow });
        db.WatchHistory.Add(new WatchHistory { UserId = user, VideoId = video, PositionMs = 100, DurationMs = 1000,
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, SourcePartition = 0, SourceOffset = 0 });
        await db.SaveChangesAsync();
        await fixture.Connection.GetDatabase().StringSetAsync(WatchHistoryCache.StateKey(user), "wrong-type");
        var response = await client.PutAsJsonAsync($"/api/engagement/watch-history/{video}", new SaveWatchProgress(500, 1000, false));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<WatchHistoryMutation>())!.CachePending);
        var fallback = await client.GetFromJsonAsync<WatchHistoryItem>($"/api/engagement/watch-history/{video}");
        Assert.Equal(100, fallback!.PositionMs);
    }
}
