using System.Text.Json;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Data.Entities;
using StreamForge.Transcoding.Worker.Options;
using StreamForge.Transcoding.Worker.Services;

namespace StreamForge.Transcoding.UnitTests;

public sealed class OutcomeMessageFactoryTests
{
    [Fact]
    public void CreateCompleted_UsesDedicatedCompletedTopicAndVersionedPayload()
    {
        var factory = CreateFactory();
        var job = CreateJob();
        var rendition = new ProcessedRendition(
            "480p",
            854,
            480,
            "h264",
            "aac",
            "video/mp4",
            "streamforge-renditions",
            "videos/key/480p/key-480p.mp4",
            "etag",
            1234);

        var message = factory.CreateCompleted(job, [rendition], DateTimeOffset.UnixEpoch);

        Assert.Equal("video-transcoding-completed", message.Topic);
        Assert.NotEqual("video-processing", message.Topic);
        Assert.Equal(job.VideoId.ToString("D"), message.PartitionKey);
        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal("video.transcoding.completed", payload.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(job.EventId, payload.RootElement.GetProperty("causationEventId").GetGuid());
        Assert.Equal("480p", payload.RootElement.GetProperty("renditions")[0].GetProperty("tier").GetString());
    }

    [Fact]
    public void CreateFailed_UsesDedicatedFailedTopic()
    {
        var message = CreateFactory().CreateFailed(
            CreateJob(),
            "source_media_invalid",
            "The media file could not be probed.",
            DateTimeOffset.UnixEpoch);

        Assert.Equal("video-transcoding-failed", message.Topic);
        Assert.NotEqual("video-processing", message.Topic);
    }

    private static OutcomeMessageFactory CreateFactory() => new(Options.Create(new KafkaOptions
    {
        InputTopic = "video-processing",
        CompletedTopic = "video-transcoding-completed",
        FailedTopic = "video-transcoding-failed",
        DeadLetterTopic = "video-processing-dead-letter"
    }));

    private static TranscodingJob CreateJob() => new()
    {
        EventId = Guid.Parse("5adbaf16-45de-46bc-b499-24be0414125d"),
        VideoId = Guid.Parse("e2c1bb10-4340-452f-9fc6-a68cf4b12457"),
        SourceBucket = "streamforge-videos",
        SourceObjectKey = "sources/source.mp4",
        SourceEtag = "source-etag",
        CorrelationId = "correlation-123",
        AttemptCount = 1
    };
}
