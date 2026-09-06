using Microsoft.EntityFrameworkCore;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Data.Entities;
using StreamForge.Engagement.Api.Services;

namespace StreamForge.Engagement.UnitTests;

public sealed class EngagementModelTests
{
    [Fact]
    public void Reaction_UsesOneCompositeIdentityPerVideoAndUser()
    {
        using var context = Context();
        var key = context.Model.FindEntityType(typeof(Reaction))!.FindPrimaryKey()!;

        Assert.Equal([nameof(Reaction.VideoId), nameof(Reaction.UserId)], key.Properties.Select(x => x.Name));
    }

    [Fact]
    public void VideoViews_UseVideoAsAggregateIdentity()
    {
        using var context = Context();
        var key = context.Model.FindEntityType(typeof(VideoView))!.FindPrimaryKey()!;

        Assert.Equal(nameof(VideoView.VideoId), Assert.Single(key.Properties).Name);
    }

    [Fact]
    public void CommentCursor_RoundTripsAndRejectsMalformedInput()
    {
        var codec = new CommentCursorCodec();
        var expectedTime = DateTimeOffset.Parse("2026-09-06T10:00:00Z");
        var expectedId = Guid.NewGuid();

        var decoded = codec.Decode(codec.Encode(expectedTime, expectedId));

        Assert.Equal(expectedTime, decoded.CreatedAtUtc);
        Assert.Equal(expectedId, decoded.Id);
        Assert.Throws<EngagementRequestException>(() => codec.Decode("not-a-cursor"));
    }

    [Theory]
    [InlineData(9_999, 9_999, false)]
    [InlineData(10_000, 0, true)]
    [InlineData(1, 10_000, true)]
    [InlineData(0, 10_000, false)]
    public void ViewBuffer_FlushesAtFiveMinutesOrTenThousandEvents(
        int bufferedCount,
        int elapsedMilliseconds,
        bool expected)
    {
        var start = DateTimeOffset.Parse("2026-09-06T10:00:00Z");
        var deadline = start.AddSeconds(10);

        Assert.Equal(expected, ViewAggregationConsumer.ShouldFlush(
            bufferedCount, start.AddMilliseconds(elapsedMilliseconds), deadline, 10_000));
    }

    private static EngagementDbContext Context() => new(new DbContextOptionsBuilder<EngagementDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
