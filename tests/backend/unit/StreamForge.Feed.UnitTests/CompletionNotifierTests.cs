using StreamForge.Feed.Api.Services;

namespace StreamForge.Feed.UnitTests;

public sealed class CompletionNotifierTests
{
    [Fact]
    public async Task Publish_NotifiesOnlyMatchingVideo()
    {
        var notifier = new CompletionNotifier();
        var videoId = Guid.NewGuid();
        using var matching = notifier.Subscribe(videoId);
        using var other = notifier.Subscribe(Guid.NewGuid());
        var notification = new CompletionNotification(videoId, DateTimeOffset.UtcNow);

        notifier.Publish(notification);

        Assert.Equal(notification, await matching.Reader.ReadAsync());
        Assert.False(other.Reader.TryRead(out _));
    }
}
