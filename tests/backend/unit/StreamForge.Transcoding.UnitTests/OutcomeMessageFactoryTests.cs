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

        var prefix = $"videos/{job.VideoId:N}/hls/";
        var result = new ProcessedTranscodingResult([rendition], new ProcessedHlsPackage(
            "streamforge-renditions", prefix, prefix + "master.m3u8", "master-etag", "fmp4", 4, 10, 100,
            [new ProcessedHlsVariant("480p",854,480,30,"h264","aac","avc1.4d401f,mp4a.40.2",1_628_000,1_200_000,prefix+"480p/index.m3u8","etag",3,100)]));
        var message = factory.CreateCompleted(job, result, DateTimeOffset.UnixEpoch);

        Assert.Equal("video-transcoding-completed", message.Topic);
        Assert.NotEqual("video-processing", message.Topic);
        Assert.Equal(job.VideoId.ToString("D"), message.PartitionKey);
        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal("video.transcoding.completed", payload.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(job.EventId, payload.RootElement.GetProperty("causationEventId").GetGuid());
        Assert.Equal("480p", payload.RootElement.GetProperty("renditions")[0].GetProperty("tier").GetString());
        Assert.Equal(2, payload.RootElement.GetProperty("eventVersion").GetInt32());
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
