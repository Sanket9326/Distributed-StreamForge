using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StreamForge.Feed.Api.Data;
using StreamForge.Feed.Api.Models;
using StreamForge.Feed.Api.Options;
using StreamForge.Feed.Api.Services;

namespace StreamForge.Feed.UnitTests;

public sealed class FeedEventProjectorTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ProjectAsync_JoinsOutOfOrderEventsAndDeduplicatesReplay()
    {
        var factory = CreateFactory();
        var projector = new FeedEventProjector(
            factory,
            Options.Create(new KafkaOptions()),
            Options.Create(new ObjectStorageOptions()),
            TimeProvider.System,
            NullLogger<FeedEventProjector>.Instance);
        var videoId = Guid.NewGuid();
        var completed = Completed(videoId);
        var uploaded = Uploaded(videoId);

        var completionResult = await projector.ProjectAsync(
            Envelope("video-transcoding-completed", 0, completed),
            CancellationToken.None);
        await projector.ProjectAsync(
            Envelope("video-processing", 0, uploaded),
            CancellationToken.None);
        await projector.ProjectAsync(
            Envelope("video-processing", 1, uploaded),
            CancellationToken.None);

        await using var dbContext = await factory.CreateDbContextAsync();
        var video = await dbContext.Videos.Include(candidate => candidate.Renditions).SingleAsync();
        Assert.True(completionResult.CompletionRecorded);
        Assert.True(video.HasMetadata);
        Assert.True(video.HasCompletion);
        Assert.Equal("Out-of-order demo", video.Title);
        Assert.Equal(2, video.Renditions.Count);
        Assert.Equal(3, await dbContext.ConsumedMessages.CountAsync());
        Assert.Equal(1, await dbContext.ConsumedMessages.CountAsync(message =>
            message.RejectionCode == "duplicate_event"));
    }

    [Fact]
    public async Task ProjectAsync_RejectsRenditionOutsideConfiguredBucket()
    {
        var factory = CreateFactory();
        var projector = new FeedEventProjector(
            factory,
            Options.Create(new KafkaOptions()),
            Options.Create(new ObjectStorageOptions()),
            TimeProvider.System,
            NullLogger<FeedEventProjector>.Instance);
        var videoId = Guid.NewGuid();
        var completed = Completed(videoId) with
        {
            Renditions = [Completed(videoId).Renditions[0] with { Bucket = "another-private-bucket" }]
        };

        var result = await projector.ProjectAsync(
            Envelope("video-transcoding-completed", 0, completed),
            CancellationToken.None);

        await using var dbContext = await factory.CreateDbContextAsync();
        Assert.False(result.CompletionRecorded);
        Assert.Empty(dbContext.Videos);
        Assert.Equal(
            "invalid_completed_event",
            (await dbContext.ConsumedMessages.SingleAsync()).RejectionCode);
    }

    private static ConsumedEnvelope Envelope(string topic, long offset, object payload) => new(
        topic,
        0,
        offset,
        null,
        JsonSerializer.Serialize(payload, SerializerOptions));

    private static VideoUploadedV1 Uploaded(Guid videoId) => new(
        Guid.NewGuid(),
        VideoUploadedV1.Type,
        VideoUploadedV1.Version,
        DateTimeOffset.UtcNow.AddMinutes(-2),
        videoId,
        "streamforge-videos",
        $"sources/{videoId:N}.mp4",
        "source-etag",
        "demo.mp4",
        "video/mp4",
        100,
        "Out-of-order demo",
        "Description",
        ["dotnet"],
        null,
        DateTimeOffset.UtcNow.AddMinutes(-2),
        "correlation");

    private static VideoTranscodingCompletedV1 Completed(Guid videoId) => new(
        Guid.NewGuid(),
        VideoTranscodingCompletedV1.Type,
        VideoTranscodingCompletedV1.Version,
        DateTimeOffset.UtcNow,
        Guid.NewGuid(),
        videoId,
        "streamforge-videos",
        $"sources/{videoId:N}.mp4",
        "source-etag",
        [
            Rendition(videoId, "480p", 854, 480),
            Rendition(videoId, "1080p", 1920, 1080)
        ],
        "correlation");

    private static RenditionV1 Rendition(Guid videoId, string tier, int width, int height) => new(
        tier,
        width,
        height,
        "h264",
        "aac",
        "video/mp4",
        "streamforge-renditions",
        $"videos/{videoId:N}/{tier}/{videoId:N}-{tier}.mp4",
        "etag",
        100);

    private static IDbContextFactory<FeedDbContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<FeedDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PooledDbContextFactory<FeedDbContext>(options);
    }
}
