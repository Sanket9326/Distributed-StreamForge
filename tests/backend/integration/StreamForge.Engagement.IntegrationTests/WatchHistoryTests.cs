using System.Globalization;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Services;

namespace StreamForge.Engagement.IntegrationTests;

public sealed class WatchHistoryTests(SubscriptionFixture fixture) : IClassFixture<SubscriptionFixture>
{
    private WatchHistoryCache Cache => new(fixture.Scripts, fixture.Contexts, new());
    private WatchHistoryProjector Projector => new(fixture.Contexts, TimeProvider.System);
    private WatchHistoryRetryQueue Queue(TimeProvider? clock = null) => new(fixture.Scripts, clock ?? TimeProvider.System);
    private WatchHistoryConsumer Consumer(WatchHistoryProjector? projector = null, IWatchHistoryPublisher? publisher = null) =>
        new(projector ?? Projector, Cache, Queue(), publisher ?? fixture.Publisher, new(), Options.Create(fixture.Kafka),
            TimeProvider.System, NullLogger<WatchHistoryConsumer>.Instance);
    private static UserWatchProgressSavedV1 Event(Guid user, Guid video, long position = 120_000) =>
        new(Guid.NewGuid(), UserWatchProgressSavedV1.Type, 1, DateTimeOffset.Parse("2026-09-15T10:00:00Z"),
            user, video, position, 600_000, false, "history-test");
    private ConsumeResult<string, string> Record(UserWatchProgressSavedV1 message, long offset, int partition = 0,
        WatchHistoryRetryEnvelope? retry = null)
    {
        var headers = new Headers();
        if (retry is not null) headers.Add(WatchHistoryConsumer.RetryHeader, JsonSerializer.SerializeToUtf8Bytes(retry, WatchHistoryCacheState.Json));
        return new() { Topic = fixture.Kafka.WatchHistoryTopic, Partition = partition, Offset = offset,
            Message = new() { Key = message.Key, Value = JsonSerializer.Serialize(message, WatchHistoryCacheState.Json), Headers = headers } };
    }
    private WatchHistoryRetryEnvelope Envelope(UserWatchProgressSavedV1 message, long offset, int attempt, long due) =>
        new(message.EventId, JsonSerializer.Serialize(message, WatchHistoryCacheState.Json), [], message.Key,
            fixture.Kafka.WatchHistoryTopic, 0, offset.ToString(CultureInfo.InvariantCulture), attempt, due,
            DateTimeOffset.UtcNow, "TimeoutException");
    private async Task ClearQueueAsync() => await fixture.Connection.GetDatabase().KeyDeleteAsync(WatchHistoryRetryQueue.Keys);

    [Fact]
    public async Task ConcurrentProjection_PreservesNewestOriginalOffsetAndOneRow()
    {
        var user = Guid.NewGuid(); var video = Guid.NewGuid();
        var messages = Enumerable.Range(0, 12).Select(index => Event(user, video, index * 1000)
            with { OccurredAtUtc = DateTimeOffset.UtcNow.AddSeconds(index) }).ToArray();
        await Task.WhenAll(messages.Select((message, index) => Projector.ProjectAsync(
            message, fixture.Kafka.WatchHistoryTopic, 0, 17000 + index, default)));
        await using var db = await fixture.Contexts.CreateDbContextAsync();
        var row = Assert.Single(await db.WatchHistory.Where(x => x.UserId == user).ToListAsync());
        Assert.Equal(17011, row.SourceOffset);
        Assert.Equal(11000, row.PositionMs);
        // A duplicate overlapping a rebalance must neither insert another row nor change its version.
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Projector.ProjectAsync(
            messages[0], fixture.Kafka.WatchHistoryTopic, 0, 17000, default)));
        Assert.Equal(12, await db.ConsumedMessages.CountAsync(x => messages.Select(m => m.EventId).Contains(x.EventId)));
    }

    [Fact]
    public async Task LostCache_RebuildsPaginatedHistoryFromDurableRows()
    {
        var user = Guid.NewGuid(); var video = Guid.NewGuid();
        var row = await Projector.ProjectAsync(Event(user, video), fixture.Kafka.WatchHistoryTopic, 0, 18001, default);
        await Cache.ApplyAsync(WatchHistoryCacheState.From(row), default);
        Assert.Single((await Cache.GetPageAsync(user, 20, null, default)).Items);
        await fixture.Connection.GetDatabase().KeyDeleteAsync(new[] {
            WatchHistoryCache.StateKey(user), WatchHistoryCache.OrderKey(user), WatchHistoryCache.ReadyKey(user),
            (StackExchange.Redis.RedisKey)$"{WatchHistoryCache.ReadyKey(user)}:generation" });
        var rebuilt = Assert.Single((await Cache.GetPageAsync(user, 20, null, default)).Items);
        Assert.Equal(video, rebuilt.VideoId);
        Assert.Equal("18001", rebuilt.SourceOffset);
        Assert.Equal(row.PositionMs, rebuilt.PositionMs);
    }

    [Fact]
    public async Task MalformedRetry_CannotReplaceProgressOrScheduleAnotherAttempt()
    {
        await ClearQueueAsync();
        var message = Event(Guid.NewGuid(), Guid.NewGuid());
        var publisher = new RecordingPublisher();
        using var consumer = Consumer(publisher: publisher);
        // Retries may not claim another partition or use their new transport offset as their version.
        await consumer.HandleAsync(Record(message, 19002, retry: Envelope(message, 19001, 1, 0)
            with { OriginalPartition = 2 }), default);
        await consumer.HandleAsync(Record(message, 19002, retry: Envelope(message, 19002, 1, 0)), default);
        Assert.Equal(2, publisher.DeadLetters.Count);
        Assert.All(publisher.DeadLetters, x => Assert.Equal("invalid_watch_history_event", x.Reason));
        Assert.Empty(await Queue().ClaimAsync("invalid-retry", default));
        await using var db = await fixture.Contexts.CreateDbContextAsync();
        Assert.False(await db.WatchHistory.AnyAsync(x => x.UserId == message.UserId));
    }

    [Fact]
    public async Task RewatchAndDelayedRetry_PreserveOneRowAndNewestOriginalPosition()
    {
        var user = Guid.NewGuid(); var video = Guid.NewGuid();
        var old = Event(user, video);
        var latest = Event(user, video, 300_000) with { OccurredAtUtc = old.OccurredAtUtc.AddMinutes(5) };
        const long offset = 9007199254740993;
        using var consumer = Consumer();
        await consumer.HandleAsync(Record(latest, offset + 1), default);
        await consumer.HandleAsync(Record(old, offset + 10, retry: Envelope(old, offset, 1, 0)), default);
        Assert.Equal(300_000, (await Cache.GetAsync(user, video, default))!.PositionMs);
        var rewind = Event(user, video, 30_000) with { OccurredAtUtc = latest.OccurredAtUtc.AddMinutes(1) };
        await consumer.HandleAsync(Record(rewind, offset + 2), default);
        await consumer.HandleAsync(Record(rewind, offset + 20, retry: Envelope(rewind, offset + 2, 1, 0)), default);
        await using var db = await fixture.Contexts.CreateDbContextAsync();
        var row = Assert.Single(await db.WatchHistory.Where(x => x.UserId == user).ToListAsync());
        Assert.Equal(30_000, row.PositionMs);
        Assert.Equal(latest.OccurredAtUtc, row.CreatedAtUtc);
        Assert.Equal(offset + 2, row.SourceOffset);
        Assert.Equal((offset + 2).ToString(), (await Cache.GetAsync(user, video, default))!.SourceOffset);
    }

    [Fact]
    public async Task CacheHydration_PreservesAcceptedChangesPaginatesAndIsolatesUsers()
    {
        var user = Guid.NewGuid(); var video = Guid.NewGuid();
        Assert.Null(await Cache.GetAsync(user, video, default));
        var message = Event(user, video);
        var row = await Projector.ProjectAsync(message, fixture.Kafka.WatchHistoryTopic, 0, 10001, default);
        await Cache.ApplyAsync(WatchHistoryCacheState.From(row) with { PositionMs = 250_000, SourceOffset = "10002", Confirmed = false }, default);
        await Projector.ProjectAsync(Event(user, Guid.NewGuid()) with { OccurredAtUtc = message.OccurredAtUtc.AddMinutes(-1) },
            fixture.Kafka.WatchHistoryTopic, 0, 10003, default);
        var first = await Cache.GetPageAsync(user, 1, null, default);
        Assert.Equal(250_000, Assert.Single(first.Items).PositionMs);
        Assert.NotNull(first.NextCursor);
        var second = await Cache.GetPageAsync(user, 1, first.NextCursor, default);
        Assert.NotEqual(video, Assert.Single(second.Items).VideoId);
        Assert.Null(second.NextCursor);
        Assert.Empty((await Cache.GetPageAsync(Guid.NewGuid(), 20, null, default)).Items);
        await Cache.ApplyAsync(WatchHistoryCacheState.From(row), default);
        Assert.Equal(250_000, (await Cache.GetAsync(user, video, default))!.PositionMs);
        Assert.False(await fixture.Connection.GetDatabase().KeyExistsAsync(WatchHistoryCache.AbsentKey(user, video)));
    }

    [Fact]
    public async Task DuplicateDelivery_RepairsLostRedisState()
    {
        var message = Event(Guid.NewGuid(), Guid.NewGuid());
        using var consumer = Consumer();
        await consumer.HandleAsync(Record(message, 11001), default);
        await fixture.Connection.GetDatabase().KeyDeleteAsync(new[] { WatchHistoryCache.StateKey(message.UserId), WatchHistoryCache.OrderKey(message.UserId) });
        await consumer.HandleAsync(Record(message, 11002, retry: Envelope(message, 11001, 1, 0)), default);
        Assert.Equal(120_000, (await Cache.GetAsync(message.UserId, message.VideoId, default))!.PositionMs);
    }

    [Fact]
    public async Task RetryClaims_AreOrderedExclusiveRecoverableAndAttemptScoped()
    {
        await ClearQueueAsync();
        var clock = new TestClock(); var queue = Queue(clock);
        var first = Envelope(Event(Guid.NewGuid(), Guid.NewGuid()), 12001, 1, clock.Now.ToUnixTimeMilliseconds());
        var later = Envelope(Event(Guid.NewGuid(), Guid.NewGuid()), 12002, 1, clock.Now.ToUnixTimeMilliseconds() + 5000);
        await queue.EnqueueAsync(later, default); await queue.EnqueueAsync(first, default); await queue.EnqueueAsync(first, default);
        Assert.Equal(first.EventId, Assert.Single(await queue.ClaimAsync("a", default)).EventId);
        Assert.Empty(await queue.ClaimAsync("b", default));
        Assert.False(await queue.ControlAsync(first.RetryId, "b", "ack", default));
        clock.Now += TimeSpan.FromSeconds(31);
        var recovered = await queue.ClaimAsync("b", default);
        Assert.Equal(2, recovered.Count);
        Assert.Equal(later.EventId, recovered[0].EventId);
        Assert.False(await queue.ControlAsync(first.RetryId, "a", "ack", default));
        var next = first with { Attempt = 2, DueAtUnixMs = clock.Now.ToUnixTimeMilliseconds() };
        await queue.EnqueueAsync(next, default);
        Assert.True(await queue.ControlAsync(first.RetryId, "b", "ack", default));
        Assert.Equal(next.RetryId, Assert.Single(await queue.ClaimAsync("c", default)).RetryId);
        await queue.EnqueueAsync(first, default); // Replay after publication does not recreate the same attempt.
        Assert.Empty(await queue.ClaimAsync("d", default));
        await ClearQueueAsync();
    }

    [Fact]
    public async Task TransientFailure_QueuesCompleteMessageAndRetainsOriginalVersionOnKafkaRepublication()
    {
        await ClearQueueAsync();
        var message = Event(Guid.NewGuid(), Guid.NewGuid());
        var delivery = await fixture.Publisher.PublishWatchHistoryAsync(message, default);
        using var reader = fixture.Reader();
        reader.Assign(new TopicPartitionOffset(fixture.Kafka.WatchHistoryTopic, delivery.Partition, delivery.Offset));
        var record = reader.Consume(TimeSpan.FromSeconds(15));
        using var failing = Consumer(new(new UnavailableDatabase(), TimeProvider.System));
        await failing.HandleAsync(record, default);
        var clock = new TestClock { Now = DateTimeOffset.UtcNow.AddMinutes(1) };
        var queue = Queue(clock);
        var envelope = Assert.Single(await queue.ClaimAsync("publisher", default));
        Assert.Equal(message.EventId, envelope.EventId);
        Assert.Equal(delivery.Offset.ToString(), envelope.OriginalOffset);
        Assert.Equal(record.Message.Value, envelope.Payload);
        await fixture.Publisher.RepublishWatchHistoryAsync(envelope, default);
        var replay = reader.Consume(TimeSpan.FromSeconds(15));
        using var consumer = Consumer();
        await consumer.HandleAsync(replay, default);
        var saved = (await Cache.GetAsync(message.UserId, message.VideoId, default))!;
        Assert.Equal(delivery.Offset.ToString(), saved.SourceOffset);
        Assert.Equal(delivery.Partition, saved.SourcePartition);
        await ClearQueueAsync();
    }

    [Fact]
    public async Task RetryEnqueueFailure_PropagatesWithoutRecordingReceipt()
    {
        await ClearQueueAsync();
        await fixture.Connection.GetDatabase().StringSetAsync(WatchHistoryRetryQueue.Keys[0], "wrong-type");
        var message = Event(Guid.NewGuid(), Guid.NewGuid());
        using var consumer = Consumer(new(new UnavailableDatabase(), TimeProvider.System));
        await Assert.ThrowsAnyAsync<Exception>(() => consumer.HandleAsync(Record(message, 13001), default));
        await using var db = await fixture.Contexts.CreateDbContextAsync();
        Assert.False(await db.ConsumedMessages.AnyAsync(x => x.EventId == message.EventId));
        await ClearQueueAsync();
    }

    [Fact]
    public async Task ExhaustedRetry_DeadLettersAndClearsOnlyMatchingUnconfirmedProgress()
    {
        var message = Event(Guid.NewGuid(), Guid.NewGuid());
        var state = new WatchHistoryCacheState(message.UserId, message.VideoId, 120_000, 600_000, false,
            message.OccurredAtUtc, message.OccurredAtUtc, 0, "14001", SubscriptionCursorCodec.SortTime(message.OccurredAtUtc), false);
        await Cache.ApplyAsync(state, default);
        var publisher = new RecordingPublisher();
        using var consumer = Consumer(new(new UnavailableDatabase(), TimeProvider.System), publisher);
        await consumer.HandleAsync(Record(message, 14010, retry: Envelope(message, 14001, 8, 0)), default);
        Assert.Equal("processing_retries_exhausted", Assert.Single(publisher.DeadLetters).Reason);
        Assert.Null(await Cache.GetAsync(message.UserId, message.VideoId, default));
        await Cache.ApplyAsync(state with { PositionMs = 300_000, SourceOffset = "14002" }, default);
        await consumer.HandleAsync(Record(message, 14011, retry: Envelope(message, 14001, 8, 0)), default);
        Assert.Equal(300_000, (await Cache.GetAsync(message.UserId, message.VideoId, default))!.PositionMs);
    }

    [Fact]
    public async Task PublicationFailure_RetainsTheRetryForAnotherWorker()
    {
        await ClearQueueAsync();
        var clock = new TestClock(); var queue = Queue(clock);
        var envelope = Envelope(Event(Guid.NewGuid(), Guid.NewGuid()), 15001, 1, 0);
        await queue.EnqueueAsync(envelope, default);
        var claimed = Assert.Single(await queue.ClaimAsync("a", default));
        using var worker = new WatchHistoryRetryWorker(queue, new RecordingPublisher { FailPublication = true }, new(),
            NullLogger<WatchHistoryRetryWorker>.Instance);
        await worker.PublishAsync(claimed, "a", default);
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(envelope.RetryId, Assert.Single(await queue.ClaimAsync("b", default)).RetryId);
        await ClearQueueAsync();
    }

    [Fact]
    public async Task MalformedEvent_RequiresDeadLetterAcknowledgement()
    {
        var record = Record(Event(Guid.NewGuid(), Guid.NewGuid()), 16001);
        record.Message.Value = "invalid";
        var publisher = new RecordingPublisher { FailPublication = true };
        using var consumer = Consumer(publisher: publisher);
        await Assert.ThrowsAsync<TimeoutException>(() => consumer.HandleAsync(record, default));
        publisher.FailPublication = false;
        await consumer.HandleAsync(record, default);
        Assert.Equal("invalid_watch_history_event", Assert.Single(publisher.DeadLetters).Reason);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class UnavailableDatabase : IDbContextFactory<EngagementDbContext>
    {
        public EngagementDbContext CreateDbContext() => throw new TimeoutException("Simulated database outage");
    }
    private sealed class RecordingPublisher : IWatchHistoryPublisher
    {
        public bool FailPublication { get; set; }
        public List<WatchHistoryDeadLetterV1> DeadLetters { get; } = [];
        public Task<WatchHistoryDelivery> PublishWatchHistoryAsync(UserWatchProgressSavedV1 message, CancellationToken ct) => throw new NotSupportedException();
        public Task RepublishWatchHistoryAsync(WatchHistoryRetryEnvelope envelope, CancellationToken ct) =>
            FailPublication ? Task.FromException(new TimeoutException()) : Task.CompletedTask;
        public Task PublishWatchHistoryDeadLetterAsync(WatchHistoryDeadLetterV1 message, string key, CancellationToken ct)
        {
            if (FailPublication) throw new TimeoutException();
            DeadLetters.Add(message); return Task.CompletedTask;
        }
    }
}
