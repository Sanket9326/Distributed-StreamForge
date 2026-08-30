using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using StreamForge.Transcoding.Worker.Data;
using StreamForge.Transcoding.Worker.Models;

namespace StreamForge.Transcoding.IntegrationTests;

public sealed class TranscodingFlowTests(TranscodingWorkerFactory factory)
    : IClassFixture<TranscodingWorkerFactory>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DuplicateUploadEvent_PublishesOneCompletedOutcomeOnDedicatedTopic()
    {
        var upload = CreateEvent("source.mp4");
        await PublishAsync(upload);
        await PublishAsync(upload);

        var completed = await ConsumeMatchingAsync(
            TranscodingWorkerFactory.CompletedTopic,
            root => root.GetProperty("causationEventId").GetGuid() == upload.EventId,
            TimeSpan.FromSeconds(20));

        Assert.Equal("video.transcoding.completed", completed.GetProperty("eventType").GetString());
        Assert.Equal(upload.VideoId, completed.GetProperty("videoId").GetGuid());
        Assert.Equal("480p", completed.GetProperty("renditions")[0].GetProperty("tier").GetString());
        await using var dbContext = CreateDbContext();
        Assert.Equal(1, await dbContext.Jobs.CountAsync(job => job.EventId == upload.EventId));
        Assert.Equal(1, await dbContext.OutboxMessages.CountAsync(message =>
            message.VideoId == upload.VideoId && message.Topic == TranscodingWorkerFactory.CompletedTopic));
        Assert.Equal(0, await dbContext.OutboxMessages.CountAsync(message =>
            message.VideoId == upload.VideoId && message.Topic == TranscodingWorkerFactory.InputTopic));
    }

    [Fact]
    public async Task InvalidMediaJob_PublishesFailedOutcomeOnDedicatedTopic()
    {
        var upload = CreateEvent("invalid-source.mp4");
        await PublishAsync(upload);

        var failed = await ConsumeMatchingAsync(
            TranscodingWorkerFactory.FailedTopic,
            root => root.GetProperty("causationEventId").GetGuid() == upload.EventId,
            TimeSpan.FromSeconds(20));

        Assert.Equal("video.transcoding.failed", failed.GetProperty("eventType").GetString());
        Assert.Equal("source_media_invalid", failed.GetProperty("failureCode").GetString());
    }

    [Fact]
    public async Task MalformedInput_PublishesDeadLetterAndDoesNotCreateJob()
    {
        var marker = Guid.NewGuid().ToString("N");
        await PublishRawAsync(marker, "{not-json");

        var deadLetter = await ConsumeMatchingAsync(
            TranscodingWorkerFactory.DeadLetterTopic,
            root => root.GetProperty("sourceKey").GetString() == marker,
            TimeSpan.FromSeconds(20));

        Assert.Equal("invalid_json", deadLetter.GetProperty("rejectionCode").GetString());
    }

    private async Task PublishAsync(VideoUploadedV1 videoUploaded) =>
        await PublishRawAsync(videoUploaded.VideoId.ToString("D"), JsonSerializer.Serialize(videoUploaded, SerializerOptions));

    private async Task PublishRawAsync(string key, string payload)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = factory.KafkaBootstrapServers,
            Acks = Acks.All
        }).Build();
        await producer.ProduceAsync(
            TranscodingWorkerFactory.InputTopic,
            new Message<string, string> { Key = key, Value = payload });
    }

    private async Task<JsonElement> ConsumeMatchingAsync(
        string topic,
        Func<JsonElement, bool> predicate,
        TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = factory.KafkaBootstrapServers,
            GroupId = $"streamforge-transcoding-assert-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(topic);
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var consumed = consumer.Consume(TimeSpan.FromMilliseconds(250));
            if (consumed is null)
            {
                await Task.Delay(25);
                continue;
            }

            using var payload = JsonDocument.Parse(consumed.Message.Value);
            if (predicate(payload.RootElement))
            {
                return payload.RootElement.Clone();
            }
        }

        throw new TimeoutException($"No matching event was received from '{topic}'.");
    }

    private TranscodingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TranscodingDbContext>()
            .UseNpgsql(factory.PostgresConnectionString)
            .Options;
        return new TranscodingDbContext(options);
    }

    private static VideoUploadedV1 CreateEvent(string sourceName)
    {
        var now = DateTimeOffset.UtcNow;
        return new VideoUploadedV1(
            Guid.NewGuid(),
            VideoUploadedV1.Type,
            VideoUploadedV1.Version,
            now,
            Guid.NewGuid(),
            "streamforge-videos",
            $"sources/{sourceName}",
            "source-etag",
            sourceName,
            "video/mp4",
            123,
            "Test video",
            null,
            [],
            null,
            now,
            Guid.NewGuid().ToString("N"));
    }
}
