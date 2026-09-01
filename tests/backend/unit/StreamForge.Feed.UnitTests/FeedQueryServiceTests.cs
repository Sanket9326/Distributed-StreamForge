using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using StreamForge.Feed.Api.Data;
using StreamForge.Feed.Api.Data.Entities;
using StreamForge.Feed.Api.Services;

namespace StreamForge.Feed.UnitTests;

public sealed class FeedQueryServiceTests
{
    [Fact]
    public async Task GetPage_ReturnsOnlyReadyVideosInStableDescendingPages()
    {
        var factory = CreateFactory();
        var firstId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var secondId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(factory,
            ReadyVideo(firstId, "Newest", now),
            ReadyVideo(secondId, "Older", now.AddMinutes(-1)),
            new FeedVideo
            {
                Id = Guid.NewGuid(),
                Title = "Still processing",
                HasMetadata = true,
                UploadedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        var service = new FeedQueryService(factory, new FeedCursorCodec(), new FakeSigner());

        var first = await service.GetPageAsync(1, null, CancellationToken.None);
        var second = await service.GetPageAsync(1, first.NextCursor, CancellationToken.None);

        Assert.Equal(firstId, Assert.Single(first.Items).Id);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(secondId, Assert.Single(second.Items).Id);
        Assert.Null(second.NextCursor);
        Assert.EndsWith("?signed=true", second.Items[0].Renditions[0].PlaybackUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task GetPage_RejectsLimitsOutsideContract(int limit)
    {
        var service = new FeedQueryService(CreateFactory(), new FeedCursorCodec(), new FakeSigner());
        var exception = await Assert.ThrowsAsync<FeedRequestException>(() =>
            service.GetPageAsync(limit, null, CancellationToken.None));
        Assert.Equal(400, exception.StatusCode);
    }

    private static FeedVideo ReadyVideo(Guid id, string title, DateTimeOffset availableAtUtc) => new()
    {
        Id = id,
        Title = title,
        Hashtags = ["video"],
        UploadedAtUtc = availableAtUtc.AddMinutes(-5),
        HasMetadata = true,
        AvailableAtUtc = availableAtUtc,
        HasCompletion = true,
        SortKey = FeedSortKey.Create(availableAtUtc, id),
        CreatedAtUtc = availableAtUtc,
        UpdatedAtUtc = availableAtUtc,
        Renditions =
        [
            new FeedRendition
            {
                VideoId = id,
                Tier = "1080p",
                Width = 1920,
                Height = 1080,
                VideoCodec = "h264",
                AudioCodec = "aac",
                ContentType = "video/mp4",
                Bucket = "streamforge-renditions",
                ObjectKey = $"videos/{id:N}/1080p/{id:N}-1080p.mp4",
                Etag = "etag",
                SizeBytes = 10
            }
        ]
    };

    private static async Task SeedAsync(
        IDbContextFactory<FeedDbContext> factory,
        params FeedVideo[] videos)
    {
        await using var dbContext = await factory.CreateDbContextAsync();
        dbContext.Videos.AddRange(videos);
        await dbContext.SaveChangesAsync();
    }

    private static IDbContextFactory<FeedDbContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<FeedDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new PooledDbContextFactory<FeedDbContext>(options);
    }

    private sealed class FakeSigner : IPlaybackUrlSigner
    {
        public Task<SignedPlaybackUrl> SignAsync(
            FeedRendition rendition,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SignedPlaybackUrl(
                $"https://storage.test/{rendition.ObjectKey}?signed=true",
                DateTimeOffset.UtcNow.AddHours(1)));
    }
}
