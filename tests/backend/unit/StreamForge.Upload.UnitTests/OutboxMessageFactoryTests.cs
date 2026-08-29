using System.Text.Json;
using StreamForge.Upload.Api.Models;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.UnitTests;

public sealed class OutboxMessageFactoryTests
{
    [Fact]
    public void Create_UsesVideoIdAsPartitionKeyAndSerializesV1Contract()
    {
        var eventId = Guid.Parse("d6dc2dd4-c355-4e74-a0b6-977d20ca9d2f");
        var videoId = Guid.Parse("fb79f32a-f61e-48a2-a560-eaed870ea40c");
        var occurredAt = new DateTimeOffset(2026, 8, 29, 10, 30, 0, TimeSpan.Zero);
        var videoUploaded = new VideoUploadedV1(
            eventId,
            VideoUploadedV1.Type,
            VideoUploadedV1.Version,
            occurredAt,
            videoId,
            "streamforge-videos",
            "sources/key.mp4",
            "etag",
            "source.mp4",
            "video/mp4",
            123,
            "Title",
            "Description",
            ["dotnet", "video"],
            OwnerId: null,
            occurredAt,
            "correlation-123");

        var outbox = new OutboxMessageFactory().Create(videoUploaded, "video-processing");

        Assert.Equal(eventId, outbox.Id);
        Assert.Equal(videoId, outbox.VideoId);
        Assert.Equal(videoId.ToString("D"), outbox.PartitionKey);
        Assert.Equal("video-processing", outbox.Topic);
        Assert.Equal(VideoUploadedV1.Type, outbox.Type);
        Assert.Equal(VideoUploadedV1.Version, outbox.Version);
        Assert.Equal(occurredAt, outbox.NextAttemptAtUtc);

        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(eventId, payload.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(videoId, payload.RootElement.GetProperty("videoId").GetGuid());
        Assert.Equal("video.uploaded", payload.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(1, payload.RootElement.GetProperty("eventVersion").GetInt32());
        Assert.Equal("dotnet", payload.RootElement.GetProperty("hashtags")[0].GetString());
    }
}
