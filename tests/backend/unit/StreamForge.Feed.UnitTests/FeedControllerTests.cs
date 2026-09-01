using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using StreamForge.Feed.Api.Controllers;
using StreamForge.Feed.Api.Data;
using StreamForge.Feed.Api.Data.Entities;
using StreamForge.Feed.Api.Services;

namespace StreamForge.Feed.UnitTests;

public sealed class FeedControllerTests
{
    [Fact]
    public async Task CompletionEvents_ImmediatelyEmitsPersistedCompletion()
    {
        var factory = CreateFactory();
        var videoId = Guid.NewGuid();
        var availableAtUtc = DateTimeOffset.UtcNow;
        await using (var dbContext = await factory.CreateDbContextAsync())
        {
            dbContext.Videos.Add(new FeedVideo
            {
                Id = videoId,
                HasCompletion = true,
                AvailableAtUtc = availableAtUtc,
                SortKey = FeedSortKey.Create(availableAtUtc, videoId),
                CreatedAtUtc = availableAtUtc,
                UpdatedAtUtc = availableAtUtc
            });
            await dbContext.SaveChangesAsync();
        }
        var controller = CreateController(factory, out var responseBody, out _);

        await controller.CompletionEvents(videoId, CancellationToken.None);

        Assert.Contains("event: completed", ReadBody(responseBody), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletionEvents_EmitsLiveNotificationForMatchingVideo()
    {
        var factory = CreateFactory();
        var videoId = Guid.NewGuid();
        var controller = CreateController(factory, out var responseBody, out var notifier);

        var streamTask = controller.CompletionEvents(videoId, CancellationToken.None);
        await Task.Yield();
        notifier.Publish(new CompletionNotification(videoId, DateTimeOffset.UtcNow));
        await streamTask;

        Assert.Contains(videoId.ToString(), ReadBody(responseBody), StringComparison.OrdinalIgnoreCase);
    }

    private static FeedController CreateController(
        IDbContextFactory<FeedDbContext> factory,
        out MemoryStream responseBody,
        out CompletionNotifier notifier)
    {
        responseBody = new MemoryStream();
        notifier = new CompletionNotifier();
        var controller = new FeedController(
            new FeedQueryService(factory, new FeedCursorCodec(), new FakeSigner()),
            notifier)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Response.Body = responseBody;
        return controller;
    }

    private static string ReadBody(MemoryStream body)
    {
        body.Position = 0;
        using var reader = new StreamReader(body, leaveOpen: true);
        return reader.ReadToEnd();
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
            CancellationToken cancellationToken) => Task.FromResult(new SignedPlaybackUrl(
                "https://storage.test/video.mp4",
                DateTimeOffset.UtcNow.AddHours(1)));
    }
}
