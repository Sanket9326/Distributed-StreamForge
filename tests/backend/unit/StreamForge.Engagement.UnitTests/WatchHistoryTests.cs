using System.Text.Json;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Services;

namespace StreamForge.Engagement.UnitTests;

public sealed class WatchHistoryTests
{
    [Theory]
    [InlineData(0, 1000, true)]
    [InlineData(1000, 1000, true)]
    [InlineData(-1, 1000, false)]
    [InlineData(1001, 1000, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 9007199254740992, false)]
    public void Progress_ValidatesDurationAndPosition(long position, long duration, bool valid) =>
        Assert.Equal(valid, new SaveWatchProgress(position, duration, false).IsValid);

    [Fact]
    public void Parser_RejectsMissingFieldsMismatchedKeysAndVersions()
    {
        var message = new UserWatchProgressSavedV1(Guid.NewGuid(), UserWatchProgressSavedV1.Type, 1,
            DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), 0, 1000, false, "test");
        var json = JsonSerializer.Serialize(message, WatchHistoryCacheState.Json);
        Assert.NotNull(WatchHistoryConsumer.Parse(message.Key, json));
        Assert.Null(WatchHistoryConsumer.Parse("wrong-key", json));
        Assert.Null(WatchHistoryConsumer.Parse(message.Key, json.Replace("\"positionMs\":0,", "")));
        Assert.Null(WatchHistoryConsumer.Parse(message.Key, json.Replace("\"isCompleted\":false,", "")));
        Assert.Null(WatchHistoryConsumer.Parse(message.Key, json.Replace("\"eventVersion\":1", "\"eventVersion\":2")));
        Assert.Null(WatchHistoryConsumer.Parse(message.Key, "null"));
        Assert.False(new SaveWatchProgress(null, 1000, false).IsValid);
    }

    [Fact]
    public void Cursor_IsBoundToUserAndHistoryNamespace()
    {
        var codec = new WatchHistoryCursorCodec(); var user = Guid.NewGuid();
        var key = $"{SubscriptionCursorCodec.SortTime(DateTimeOffset.UtcNow)}:{Guid.NewGuid():N}";
        var cursor = codec.Encode(user, key);
        Assert.Equal(key, codec.Decode(user, cursor));
        Assert.Throws<EngagementRequestException>(() => codec.Decode(Guid.NewGuid(), cursor));
        Assert.Throws<EngagementRequestException>(() => codec.Decode(user, new SubscriptionCursorCodec().Encode(user, false, key)));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(5, 32)]
    [InlineData(8, 60)]
    public void RetryDelay_UsesFullJitterAndCapsTheDelay(int attempt, int cap)
    {
        Assert.Equal(TimeSpan.Zero, WatchHistoryRetryQueue.Delay(attempt, 0));
        Assert.Equal(TimeSpan.FromSeconds(cap / 2.0), WatchHistoryRetryQueue.Delay(attempt, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(cap), WatchHistoryRetryQueue.Delay(attempt, 1));
    }
}
