using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Infrastructure.Redis;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Options;
using StreamForge.Engagement.Api.Services;
using Testcontainers.PostgreSql;

namespace StreamForge.Engagement.IntegrationTests;

public sealed class SubscriptionFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.6-alpine")
        .WithDatabase("subscriptions").WithUsername("test").WithPassword("integration-password").Build();
    public readonly IContainer Redis = new ContainerBuilder("redis:8.2.1-alpine")
        .WithPortBinding(FreePort(), 6379).WithCommand("redis-server", "--save", "", "--appendonly", "no")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping")).Build();
    private readonly IContainer kafka;
    public KafkaOptions Kafka { get; }
    public string DatabaseConnection => postgres.GetConnectionString();
    public string RedisConnection => $"{Redis.Hostname}:{Redis.GetMappedPublicPort(6379)}";
    public PooledDbContextFactory<EngagementDbContext> Contexts { get; private set; } = null!;
    public ConnectionMultiplexer Connection { get; private set; } = null!;
    public LuaScriptExecutor Scripts { get; private set; } = null!;
    public SubscriptionCache Cache { get; private set; } = null!;
    public SubscriptionProjector Projector { get; private set; } = null!;
    public EngagementKafkaPublisher Publisher { get; private set; } = null!;
    public SubscriptionConsumer Consumer { get; private set; } = null!;

    public SubscriptionFixture()
    {
        var port = FreePort();
        Kafka = new KafkaOptions { BootstrapServers = $"127.0.0.1:{port}", PartitionCount = 3, ReplicationFactor = 1 };
        kafka = new ContainerBuilder("apache/kafka:4.3.1").WithPortBinding(port, 9094)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_LISTENERS", "INTERNAL://:9092,CONTROLLER://:9093,EXTERNAL://:9094")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", $"INTERNAL://localhost:9092,EXTERNAL://127.0.0.1:{port}")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "INTERNAL")
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "CONTROLLER:PLAINTEXT,INTERNAL:PLAINTEXT,EXTERNAL:PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:9093")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("/opt/kafka/bin/kafka-topics.sh",
                "--bootstrap-server", "127.0.0.1:9092", "--list")).Build();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async Task WaitForRedisAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            try { await Scripts.ReadyAsync(timeout.Token); return; }
            catch (RedisException) { await Task.Delay(200, timeout.Token); }
        }
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), Redis.StartAsync(), kafka.StartAsync());
        Contexts = new(new DbContextOptionsBuilder<EngagementDbContext>().UseNpgsql(postgres.GetConnectionString(),
            o => { o.EnableRetryOnFailure(3); o.MigrationsHistoryTable("__ef_migrations_history", EngagementDbContext.Schema); }).Options);
        await using var db = await Contexts.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Connection = await ConnectionMultiplexer.ConnectAsync(
            $"{Redis.Hostname}:{Redis.GetMappedPublicPort(6379)},allowAdmin=true,asyncTimeout=1000");
        Scripts = new(Connection);
        Cache = new(Scripts, Contexts, new());
        Projector = new(Contexts, TimeProvider.System);
        Publisher = new(Options.Create(Kafka));
        Consumer = new(Projector, Cache, Publisher, new(), Options.Create(Kafka), TimeProvider.System,
            NullLogger<SubscriptionConsumer>.Instance);
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = Kafka.BootstrapServers }).Build();
        await admin.CreateTopicsAsync(new[] { Kafka.SubscriptionTopic, Kafka.SubscriptionDeadLetterTopic,
            Kafka.WatchHistoryTopic, Kafka.WatchHistoryDeadLetterTopic }.Select(
            name => new TopicSpecification { Name = name, NumPartitions = 3, ReplicationFactor = 1 }));
    }

    public SubscriptionService Service(IHttpClientFactory? clients = null, ISubscriptionPublisher? publisher = null) =>
        new(Cache, publisher ?? Publisher, Contexts, clients ?? new Profiles(), TimeProvider.System,
            NullLogger<SubscriptionService>.Instance);

    public IConsumer<string, string> Reader(string? group = null) => new ConsumerBuilder<string, string>(new ConsumerConfig
    {
        BootstrapServers = Kafka.BootstrapServers, GroupId = group ?? Guid.NewGuid().ToString("N"),
        EnableAutoCommit = false, EnableAutoOffsetStore = false, AutoOffsetReset = AutoOffsetReset.Earliest
    }).Build();

    public async Task DisposeAsync()
    {
        Consumer.Dispose();
        Publisher.Dispose();
        await Connection.DisposeAsync();
        await Task.WhenAll(Redis.DisposeAsync().AsTask(), kafka.DisposeAsync().AsTask(), postgres.DisposeAsync().AsTask());
    }

    public sealed class Profiles(bool exists = true) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, false) { BaseAddress = new Uri("http://identity/") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var id = Guid.Parse(request.RequestUri!.Query.Split('=')[1]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(exists ? new[] { new { id, username = "creator" } } : []) });
        }
    }
}

public sealed class SubscriptionTests(SubscriptionFixture fixture) : IClassFixture<SubscriptionFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task HostedConsumer_RestartsFromItsCommittedOffsetAndPreservesRecordedOrder()
    {
        var user = Guid.NewGuid(); var creator = Guid.NewGuid();
        var settings = new KafkaOptions {
            BootstrapServers = fixture.Kafka.BootstrapServers,
            SubscriptionConsumerGroupId = "restart-" + Guid.NewGuid().ToString("N")
        };
        var service = fixture.Service();
        await service.ChangeAsync(user, creator, true, false, "first", Ct);
        var removed = await service.ChangeAsync(user, creator, false, false, "second", Ct);
        await RunUntilPersistedAsync(removed, settings);
        var subscribed = await service.ChangeAsync(user, creator, true, false, "third", Ct);
        await RunUntilPersistedAsync(subscribed, settings);
        Assert.True(Assert.Single(await fixture.Cache.GetStatusAsync(user, [creator], Ct)).IsActive);
    }

    private async Task RunUntilPersistedAsync(SubscriptionMutation expected, KafkaOptions settings)
    {
        var startup = new StartupGate();
        startup.MarkReady();
        using var consumer = new SubscriptionConsumer(fixture.Projector, fixture.Cache, fixture.Publisher,
            startup, Options.Create(settings), TimeProvider.System, NullLogger<SubscriptionConsumer>.Instance);
        await consumer.StartAsync(Ct);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                await using var db = await fixture.Contexts.CreateDbContextAsync(deadline.Token);
                var row = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(
                    x => x.SubscriberId == expected.SubscriberId && x.CreatorId == expected.CreatorId, deadline.Token);
                if (row?.SourceOffset == long.Parse(expected.SourceOffset, CultureInfo.InvariantCulture))
                {
                    Assert.Equal(expected.IsActive, row.IsActive);
                    break;
                }
                await Task.Delay(100, deadline.Token);
            }
        }
        finally { await consumer.StopAsync(Ct); }
    }

    [Fact]
    public async Task CommentInitialization_RejectsCountsReadBeforeAConcurrentInvalidation()
    {
        RedisKey[] keys = { "fixture:comments:" + Guid.NewGuid(), "fixture:comment-generation:" + Guid.NewGuid() };
        var before = (RedisResult[])(await fixture.Scripts.ExecuteAsync("comment-read", keys, []))!;
        await fixture.Scripts.ExecuteAsync("comment-invalidate", keys, ["new-generation"]);
        var stale = await fixture.Scripts.ExecuteAsync("comment-initialize", keys, [(string)before[1]!, "2"]);
        Assert.True(stale.IsNull);
        var current = await fixture.Scripts.ExecuteAsync("comment-initialize", keys, ["new-generation", "3"]);
        Assert.Equal(3, (long)current);
        var competing = await fixture.Scripts.ExecuteAsync("comment-initialize", keys, ["new-generation", "1"]);
        Assert.Equal(3, (long)competing);
        // Simulate a restart between the database read and cache initialization.
        await fixture.Connection.GetDatabase().KeyDeleteAsync(keys);
        Assert.True((await fixture.Scripts.ExecuteAsync("comment-initialize", keys, ["new-generation", "2"])).IsNull);
        Assert.True((await fixture.Scripts.ExecuteAsync("comment-initialize", keys, ["", "2"])).IsNull);
        var generation = (string)(await fixture.Scripts.ExecuteAsync("comment-begin", keys, ["after-restart"]))!;
        Assert.Equal(4, (long)await fixture.Scripts.ExecuteAsync("comment-initialize", keys, [generation, "4"]));
    }

    [Fact]
    public async Task KafkaAcceptance_PrecedesPersistence_AndReplayRepairsBothDirections()
    {
        var user = Guid.NewGuid(); var creator = Guid.NewGuid();
        var service = fixture.Service();
        var subscribed = await service.ChangeAsync(user, creator, true, false, "test", Ct);
        Assert.False(subscribed.CachePending);
        Assert.True(subscribed.IsActive);
        await using var db = await fixture.Contexts.CreateDbContextAsync();
        Assert.False(await db.Subscriptions.AnyAsync(x => x.SubscriberId == user));
        Assert.Equal(creator, Assert.Single((await service.GetPageAsync(user, false, 20, null, Ct)).Items).UserId);
        Assert.Equal(user, Assert.Single((await service.GetPageAsync(creator, true, 20, null, Ct)).Items).UserId);
        var removed = await service.ChangeAsync(creator, user, false, true, "remove", Ct);
        Assert.Equal(subscribed.SourcePartition, removed.SourcePartition);
        Assert.True(long.Parse(removed.SourceOffset) > long.Parse(subscribed.SourceOffset));

        using var reader = fixture.Reader();
        reader.Assign(new TopicPartitionOffset(fixture.Kafka.SubscriptionTopic, new Partition(subscribed.SourcePartition),
            new Offset(long.Parse(subscribed.SourceOffset))));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var first = reader.Consume(timeout.Token);
        await fixture.Consumer.HandleAsync(first, Ct);
        // Reconciliation of the first event must not overwrite the later accepted removal.
        Assert.False(Assert.Single(await fixture.Cache.GetStatusAsync(user, [creator], Ct)).IsActive);
        ConsumeResult<string, string> second;
        do { second = reader.Consume(timeout.Token); } while (second.Offset.Value < long.Parse(removed.SourceOffset));
        await fixture.Consumer.HandleAsync(second, Ct);
        reader.Commit(second);
        var row = await db.Subscriptions.AsNoTracking().SingleAsync(x => x.SubscriberId == user && x.CreatorId == creator);
        Assert.False(row.IsActive);
        Assert.Equal(long.Parse(removed.SourceOffset), row.SourceOffset);

        await fixture.Connection.GetDatabase().KeyDeleteAsync(new RedisKey[] {
            SubscriptionCache.StateKey(user, false), SubscriptionCache.OrderKey(user, false),
            SubscriptionCache.StateKey(creator, true), SubscriptionCache.OrderKey(creator, true) });
        await fixture.Consumer.HandleAsync(second, Ct); // Already durable, but cache must be repaired.
        Assert.False(Assert.Single(await fixture.Cache.GetStatusAsync(user, [creator], Ct)).IsActive);
        Assert.True(await fixture.Connection.GetDatabase().HashExistsAsync(SubscriptionCache.StateKey(creator, true), user.ToString("D")));
        await fixture.Consumer.HandleAsync(first, Ct); // Delayed duplicate cannot reactivate.
        Assert.False(Assert.Single(await fixture.Cache.GetStatusAsync(user, [creator], Ct)).IsActive);

        // A fresh Kafka consumer resumes after the committed position.
        var group = Guid.NewGuid().ToString("N");
        using (var before = fixture.Reader(group)) {
            before.Assign(second.TopicPartition);
            before.Commit(new[] { new TopicPartitionOffset(second.TopicPartition, second.Offset + 1) });
        }
        using var after = fixture.Reader(group);
        Assert.Equal(second.Offset + 1, Assert.Single(after.Committed(new[] { second.TopicPartition }, TimeSpan.FromSeconds(10))).Offset);
        var resubscribed = await service.ChangeAsync(user, creator, true, false, "resubscribe", Ct);
        Assert.True(resubscribed.IsActive);
    }

    [Fact]
    public async Task ExactOffsets_DelayedCallbacksAndConfirmation_DoNotRegressState()
    {
        var user = Guid.NewGuid(); var creator = Guid.NewGuid();
        var early = State(user, creator, true, 9007199254740992);
        var late = early with { IsActive = false, SourceOffset = "9007199254740993" };
        await fixture.Cache.ApplyAsync(late, Ct);
        await fixture.Cache.ApplyAsync(early, Ct);
        Assert.False(Assert.Single(await fixture.Cache.GetStatusAsync(user, [creator], Ct)).IsActive);
        await fixture.Cache.ApplyAsync(late with { Confirmed = true }, Ct);
        await fixture.Cache.ApplyAsync(early with { SourceOffset = late.SourceOffset }, Ct);
        Assert.False(Assert.Single(await fixture.Cache.GetStatusAsync(user, [creator], Ct)).IsActive);
        await Assert.ThrowsAsync<RedisServerException>(() => fixture.Cache.ApplyAsync(late with { SourcePartition = 1 }, Ct));
    }

    [Fact]
    public async Task Lists_PaginateBothDirectionsAndRebuildWithoutOverwritingPendingChanges()
    {
        var user = Guid.NewGuid(); var creator = Guid.NewGuid();
        var created = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        var rows = Enumerable.Range(0, 55).Select(i => new UserSubscription {
            SubscriberId = user, CreatorId = Guid.NewGuid(), IsActive = true, CreatedAtUtc = created,
            UpdatedAtUtc = created, SourcePartition = 0, SourceOffset = i }).ToArray();
        await using (var db = await fixture.Contexts.CreateDbContextAsync()) {
            db.Subscriptions.AddRange(rows);
            await db.SaveChangesAsync();
        }
        // Older snapshot row must not overwrite the pending removal.
        await fixture.Cache.ApplyAsync(SubscriptionCacheState.From(rows[0], false) with {
            IsActive = false, SourceOffset = "1000" }, Ct);
        // This pair exists only in Redis while the consumer is paused.
        await fixture.Cache.ApplyAsync(State(user, creator, true, 1001), Ct);
        var pages = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.Cache.GetPageAsync(user, false, 20, null, Ct)));
        Assert.All(pages, page => Assert.Equal(20, page.Items.Count));
        var all = pages[0].Items.ToList();
        var cursor = pages[0].NextCursor;
        while (cursor is not null) {
            var page = await fixture.Cache.GetPageAsync(user, false, 20, cursor, Ct);
            all.AddRange(page.Items); cursor = page.NextCursor;
        }
        Assert.Equal(55, all.Count);
        Assert.Equal(55, all.Select(x => x.UserId).Distinct().Count());
        Assert.DoesNotContain(all, x => x.UserId == rows[0].CreatorId);
        Assert.Contains(all, x => x.UserId == creator);
        Assert.Equal(user, Assert.Single((await fixture.Cache.GetPageAsync(creator, true, 20, null, Ct)).Items).UserId);
        Assert.Null(await fixture.Connection.GetDatabase().KeyTimeToLiveAsync(SubscriptionCache.StateKey(user, false)));
        await Assert.ThrowsAsync<EngagementRequestException>(() => fixture.Service().GetPageAsync(user, false, 51, null, Ct));
        await Assert.ThrowsAsync<EngagementRequestException>(() => fixture.Cache.GetPageAsync(Guid.NewGuid(), false, 20, pages[0].NextCursor, Ct));
        // Database cursor ordering agrees with the Redis ordering for tied timestamps.
        var durable = await fixture.Cache.GetDatabasePageAsync(user, false, 50, null, Ct);
        var tail = await fixture.Cache.GetDatabasePageAsync(user, false, 50, durable.NextCursor, Ct);
        Assert.Equal(55, durable.Items.Concat(tail.Items).Select(x => x.UserId).Distinct().Count());
    }

    [Fact]
    public async Task GuardedScripts_RejectWrongTypesAndLostLeases_AndRecoverAfterScriptFlush()
    {
        var user = Guid.NewGuid(); var creator = Guid.NewGuid();
        var db = fixture.Connection.GetDatabase();
        await db.StringSetAsync(SubscriptionCache.OrderKey(creator, true), "wrong-type");
        await Assert.ThrowsAsync<RedisServerException>(() => fixture.Cache.ApplyAsync(State(user, creator, true, 1), Ct));
        Assert.False(await db.KeyExistsAsync(SubscriptionCache.StateKey(user, false)));
        await db.KeyDeleteAsync(SubscriptionCache.OrderKey(creator, true));
        await fixture.Cache.ApplyAsync(State(user, creator, true, 1), Ct);
        var server = fixture.Connection.GetServer(fixture.Connection.GetEndPoints().Single());
        await server.ScriptFlushAsync();
        await fixture.Cache.ApplyAsync(State(user, creator, false, 2), Ct);
        Assert.False(Assert.Single(await fixture.Cache.GetStatusAsync(user, [creator], Ct)).IsActive);
        RedisKey[] controls = { "fixture:lease", "fixture:generation", "fixture:ready" };
        Assert.Equal(1, (int)await fixture.Scripts.ExecuteAsync("begin-rebuild", controls, ["old"]));
        await db.KeyDeleteAsync(controls[0]);
        Assert.Equal(1, (int)await fixture.Scripts.ExecuteAsync("begin-rebuild", controls, ["new"]));
        Assert.Equal(0, (int)await fixture.Scripts.ExecuteAsync("rebuild-control", controls, ["old", "complete"]));
        Assert.False(await db.KeyExistsAsync(controls[2]));
        Assert.Equal(1, (int)await fixture.Scripts.ExecuteAsync("rebuild-control", controls, ["new", "renew"]));
        await Assert.ThrowsAsync<RedisServerException>(() => fixture.Scripts.ExecuteAsync("subscription-status",
            [SubscriptionCache.StateKey(user, false)], Enumerable.Repeat((RedisValue)creator.ToString("D"), 51).ToArray()));
        var args = new List<RedisValue> { "new" };
        for (var i = 0; i < 501; i++) args.AddRange([creator.ToString("D"), JsonSerializer.Serialize(State(user, creator, true, i), SubscriptionCacheState.Json)]);
        await Assert.ThrowsAsync<RedisServerException>(() => fixture.Scripts.ExecuteAsync("subscription-merge",
            [SubscriptionCache.StateKey(user, false), SubscriptionCache.OrderKey(user, false), controls[0], controls[1]], args.ToArray()));
    }

    [Fact]
    public async Task Persistence_RetainsInactiveVersionsAndSubscriptionDates_AndEnforcesConstraints()
    {
        var user = Guid.NewGuid(); var creator = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        var message = new UserSubscriptionChangedV1(Guid.NewGuid(), UserSubscriptionChangedV1.Type, 1, now,
            user, creator, user, true, "test");
        var topic = Guid.NewGuid().ToString("N");
        await fixture.Projector.ProjectAsync(message, topic, 0, 0, Ct);
        var repeated = await fixture.Projector.ProjectAsync(message with { EventId = Guid.NewGuid(), OccurredAtUtc = now.AddDays(1) }, topic, 0, 1, Ct);
        Assert.Equal(now, repeated!.CreatedAtUtc);
        var removed = await fixture.Projector.ProjectAsync(message with { EventId = Guid.NewGuid(), IsActive = false }, topic, 0, 2, Ct);
        Assert.False(removed!.IsActive);
        var resubscribed = await fixture.Projector.ProjectAsync(message with { EventId = Guid.NewGuid(), OccurredAtUtc = now.AddDays(2) }, topic, 0, 3, Ct);
        Assert.Equal(now.AddDays(2), resubscribed!.CreatedAtUtc);
        var stale = await fixture.Projector.ProjectAsync(message with { EventId = Guid.NewGuid(), IsActive = false }, topic, 0, 1, Ct);
        Assert.True(stale!.IsActive);
        await using var db = await fixture.Contexts.CreateDbContextAsync();
        db.Subscriptions.Add(new UserSubscription { SubscriberId = user, CreatorId = user, CreatedAtUtc = now, UpdatedAtUtc = now });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Subscriptions.Add(new UserSubscription { SubscriberId = user, CreatorId = creator, CreatedAtUtc = now, UpdatedAtUtc = now });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(400, (await Assert.ThrowsAsync<EngagementRequestException>(() =>
            fixture.Service().ChangeAsync(user, user, true, false, "self", Ct))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<EngagementRequestException>(() =>
            fixture.Service(new SubscriptionFixture.Profiles(false)).ChangeAsync(user, Guid.NewGuid(), true, false, "missing", Ct))).StatusCode);
    }

    [Fact]
    public async Task RedisFailureAfterAcknowledgement_ReturnsAcceptedPending_AndReplayRepairsAfterRestart()
    {
        var user = Guid.NewGuid(); var creator = Guid.NewGuid();
        await fixture.Redis.PauseAsync();
        SubscriptionMutation result;
        try { result = await fixture.Service().ChangeAsync(user, creator, true, false, "outage", Ct); }
        finally { await fixture.Redis.UnpauseAsync(); }
        Assert.True(result.CachePending);
        using var reader = fixture.Reader();
        reader.Assign(new TopicPartitionOffset(fixture.Kafka.SubscriptionTopic, new Partition(result.SourcePartition),
            new Offset(long.Parse(result.SourceOffset))));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var record = reader.Consume(timeout.Token);
        await fixture.Consumer.HandleAsync(record, Ct);
        Assert.True(Assert.Single(await fixture.Cache.GetStatusAsync(user, [creator], Ct)).IsActive);
        await fixture.Redis.StopAsync();
        await fixture.Redis.StartAsync();
        await fixture.WaitForRedisAsync();
        await fixture.Consumer.HandleAsync(record, Ct);
        Assert.True(Assert.Single(await fixture.Cache.GetStatusAsync(user, [creator], Ct)).IsActive);
    }

    [Fact]
    public async Task MalformedEvents_ArePublishedToDeadLetterBeforeAcknowledgement()
    {
        var marker = Guid.NewGuid().ToString("N");
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = fixture.Kafka.BootstrapServers }).Build();
        var delivery = await producer.ProduceAsync(fixture.Kafka.SubscriptionTopic, new Message<string, string> { Key = marker, Value = "{}" });
        using var reader = fixture.Reader();
        reader.Assign(delivery.TopicPartitionOffset);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var record = reader.Consume(timeout.Token);
        await fixture.Consumer.HandleAsync(record, Ct);
        using var dlq = fixture.Reader();
        dlq.Assign(Enumerable.Range(0, 3).Select(p => new TopicPartitionOffset(fixture.Kafka.SubscriptionDeadLetterTopic, new Partition(p), Offset.Beginning)));
        ConsumeResult<string, string> rejected;
        do { rejected = dlq.Consume(timeout.Token); } while (rejected.Message.Key != marker);
        var letter = JsonSerializer.Deserialize<SubscriptionDeadLetterV1>(rejected.Message.Value, SubscriptionCacheState.Json)!;
        Assert.Equal(delivery.Offset.Value, letter.SourceOffset);
        Assert.Equal("invalid_subscription_event", letter.Reason);
        var failing = new SubscriptionConsumer(fixture.Projector, fixture.Cache, new RejectingDeadLetterPublisher(),
            new(), Options.Create(fixture.Kafka), TimeProvider.System, NullLogger<SubscriptionConsumer>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.HandleAsync(record, Ct));
        Assert.Null(SubscriptionConsumer.Parse(null, null));
        var invalidActor = new UserSubscriptionChangedV1(Guid.NewGuid(), UserSubscriptionChangedV1.Type, 1, DateTimeOffset.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true, "test");
        Assert.Null(SubscriptionConsumer.Parse(invalidActor.Key, JsonSerializer.Serialize(invalidActor, SubscriptionCacheState.Json)));
    }

    private static SubscriptionCacheState State(Guid subscriber, Guid creator, bool active, long offset) =>
        SubscriptionCacheState.From(new UserSubscription { SubscriberId = subscriber, CreatorId = creator,
            IsActive = active, CreatedAtUtc = DateTimeOffset.Parse("2026-09-10T12:00:00Z"),
            UpdatedAtUtc = DateTimeOffset.Parse("2026-09-10T12:00:00Z"), SourcePartition = 0, SourceOffset = offset }, false);

    private sealed class RejectingDeadLetterPublisher : ISubscriptionPublisher
    {
        public Task<SubscriptionDelivery> PublishSubscriptionAsync(UserSubscriptionChangedV1 message, CancellationToken ct) => throw new NotImplementedException();
        public Task PublishSubscriptionDeadLetterAsync(SubscriptionDeadLetterV1 message, string key, CancellationToken ct) =>
            throw new InvalidOperationException("DLQ unavailable");
    }
}
