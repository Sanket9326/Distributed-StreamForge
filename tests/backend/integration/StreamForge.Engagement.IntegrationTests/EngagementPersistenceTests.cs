using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Confluent.Kafka;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Options;
using StreamForge.Engagement.Api.Services;
using Testcontainers.PostgreSql;

namespace StreamForge.Engagement.IntegrationTests;

public sealed class EngagementPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.6-alpine")
        .WithDatabase("streamforge").WithUsername("streamforge").WithPassword("integration-password").Build();
    private readonly IContainer redis = new ContainerBuilder("redis:8.2.1-alpine")
        .WithPortBinding(6379, true)
        .WithCommand("redis-server", "--save", "", "--appendonly", "no")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping")).Build();
    private PooledDbContextFactory<EngagementDbContext> contexts = null!;
    private ConnectionMultiplexer connection = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
        contexts = new PooledDbContextFactory<EngagementDbContext>(new DbContextOptionsBuilder<EngagementDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), x =>
            {
                x.EnableRetryOnFailure(3, TimeSpan.FromSeconds(1), null);
                x.MigrationsHistoryTable("__ef_migrations_history", EngagementDbContext.Schema);
            }).Options);
        await using var dbContext = await contexts.CreateDbContextAsync();
        await dbContext.Database.MigrateAsync();
        connection = await ConnectionMultiplexer.ConnectAsync($"{redis.Hostname}:{redis.GetMappedPublicPort(6379)}");
    }

    public async Task DisposeAsync()
    {
        await connection.DisposeAsync();
        await redis.DisposeAsync();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task Comments_ArePaginatedEditableAndCountedWhileReactionsStayExclusive()
    {
        var videoId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var dbContext = await contexts.CreateDbContextAsync())
        {
            dbContext.Videos.Add(new KnownVideo { VideoId = videoId, AvailableAtUtc = DateTimeOffset.UtcNow });
            await dbContext.SaveChangesAsync();
        }
        var clock = new IncrementingTimeProvider();
        var cache = new RedisEngagementProjection(connection, contexts);
        var knownVideos = new KnownVideoService(contexts, new UnusedHttpClientFactory(), clock);
        var service = new CommentService(contexts, knownVideos, cache, new CommentCursorCodec(), clock,
            NullLogger<CommentService>.Instance);

        for (var index = 0; index < 25; index++)
            await service.CreateAsync(videoId, userId, $"Comment {index}", CancellationToken.None);

        var first = await service.GetPageAsync(videoId, 20, null, CancellationToken.None);
        var second = await service.GetPageAsync(videoId, 20, first.NextCursor, CancellationToken.None);
        Assert.Equal(25, first.TotalCount);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal(5, second.Items.Count);

        var updated = await service.UpdateAsync(first.Items[0].Id, userId, " Edited comment ", CancellationToken.None);
        Assert.Equal("Edited comment", updated.Comment.Body);
        var forbidden = await Assert.ThrowsAsync<EngagementRequestException>(() =>
            service.UpdateAsync(first.Items[0].Id, Guid.NewGuid(), "not mine", CancellationToken.None));
        Assert.Equal(403, forbidden.StatusCode);
        var deleted = await service.DeleteAsync(first.Items[0].Id, userId, CancellationToken.None);
        Assert.Equal(24, deleted.CommentCount);
        await using (var dbContext = await contexts.CreateDbContextAsync())
            Assert.False(await dbContext.Comments.AnyAsync(x => x.Id == first.Items[0].Id));

        await cache.ApplyReactionAsync(videoId, userId, "like", 1, CancellationToken.None);
        await cache.ApplyReactionAsync(videoId, userId, "dislike", 2, CancellationToken.None);
        var summary = await cache.GetSummaryAsync(videoId, CancellationToken.None);
        Assert.Equal(0, summary.LikeCount);
        Assert.Equal(1, summary.DislikeCount);
        Assert.Equal("dislike", await cache.GetReactionAsync(videoId, userId, CancellationToken.None));

        await cache.ApplyReactionAsync(videoId, userId, "like", 3, CancellationToken.None);
        await cache.ApplyReactionAsync(videoId, userId, "none", 2, CancellationToken.None);
        Assert.Equal("like", await cache.GetReactionAsync(videoId, userId, CancellationToken.None));

        var sessionId = Guid.NewGuid();
        Assert.True((await cache.IncrementViewOnceAsync(videoId, sessionId, 24, CancellationToken.None)).Counted);
        Assert.False((await cache.IncrementViewOnceAsync(videoId, sessionId, 24, CancellationToken.None)).Counted);
        Assert.True((await cache.IncrementViewOnceAsync(videoId, Guid.NewGuid(), 24, CancellationToken.None)).Counted);

        await using (var dbContext = await contexts.CreateDbContextAsync())
        {
            dbContext.Reactions.Add(new Reaction
            {
                VideoId = videoId, UserId = userId, Value = "like",
                CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow(),
                SourcePartition = 0, SourceOffset = 5
            });
            dbContext.VideoViews.Add(new VideoView
            {
                VideoId = videoId, Count = 12, UpdatedAtUtc = clock.GetUtcNow()
            });
            await dbContext.SaveChangesAsync();
        }
        var server = connection.GetServer(connection.GetEndPoints().Single());
        var keys = server.Keys(pattern: "streamforge:engagement:v1*").ToArray();
        await connection.GetDatabase().KeyDeleteAsync(keys);

        var recovered = await cache.GetSummaryAsync(videoId, CancellationToken.None);
        Assert.Equal(1, recovered.LikeCount);
        Assert.Equal(12, recovered.ViewCount);
        Assert.Equal(24, recovered.CommentCount);
        await cache.ApplyReactionAsync(videoId, userId, "dislike", 4, CancellationToken.None);
        Assert.Equal("like", await cache.GetReactionAsync(videoId, userId, CancellationToken.None));

        var aggregator = new ViewAggregationConsumer(
            contexts, cache, new StartupGate(),
            Options.Create(new KafkaOptions()), Options.Create(new EngagementOptions()),
            clock, NullLogger<ViewAggregationConsumer>.Instance);
        var firstEvent = Guid.NewGuid();
        var batch = new[]
        {
            ViewMessage(videoId, firstEvent, 0),
            ViewMessage(videoId, firstEvent, 1),
            ViewMessage(videoId, Guid.NewGuid(), 2)
        };
        TopicPartitionOffset[] committed = [];
        await aggregator.FlushAsync(batch, offsets => committed = offsets.ToArray(), CancellationToken.None);
        Assert.Equal(3, Assert.Single(committed).Offset.Value);
        await AssertViewCountAsync(videoId, 14, 2);

        await aggregator.FlushAsync(batch, _ => { }, CancellationToken.None);
        await AssertViewCountAsync(videoId, 14, 2);

        var commitFailureBatch = new[] { ViewMessage(videoId, Guid.NewGuid(), 3) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => aggregator.FlushAsync(
            commitFailureBatch, _ => throw new InvalidOperationException("commit failed"), CancellationToken.None));
        await AssertViewCountAsync(videoId, 15, 3);
        await aggregator.FlushAsync(commitFailureBatch, _ => { }, CancellationToken.None);
        await AssertViewCountAsync(videoId, 15, 3);
    }

    private async Task AssertViewCountAsync(Guid videoId, long expectedCount, int expectedReceipts)
    {
        await using var dbContext = await contexts.CreateDbContextAsync();
        Assert.Equal(expectedCount, (await dbContext.VideoViews.SingleAsync(x => x.VideoId == videoId)).Count);
        Assert.Equal(expectedReceipts,
            await dbContext.ConsumedMessages.CountAsync(x => x.Topic == "video-engagement-views"));
    }

    private static ConsumeResult<string, string> ViewMessage(Guid videoId, Guid eventId, long offset)
    {
        var message = new VideoViewQualifiedV1(
            eventId, VideoViewQualifiedV1.Type, 1, DateTimeOffset.UtcNow, videoId, "test");
        return new ConsumeResult<string, string>
        {
            Topic = "video-engagement-views",
            Partition = new Partition(0),
            Offset = new Offset(offset),
            Message = new Message<string, string>
            {
                Key = videoId.ToString("D"),
                Value = JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            }
        };
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Known videos must not call Feed.");
    }

    private sealed class IncrementingTimeProvider : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.Parse("2026-09-06T10:00:00Z");
        public override DateTimeOffset GetUtcNow() => current = current.AddMilliseconds(1);
    }
}
