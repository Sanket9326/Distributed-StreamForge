using StreamForge.Feed.Api.Services;

namespace StreamForge.Feed.UnitTests;

public sealed class FeedCursorCodecTests
{
    private readonly FeedCursorCodec codec = new();

    [Fact]
    public void EncodeDecode_RoundTripsOpaqueSortKey()
    {
        var sortKey = FeedSortKey.Create(
            new DateTimeOffset(2026, 8, 31, 10, 30, 0, TimeSpan.Zero),
            Guid.Parse("e2c1bb10-4340-452f-9fc6-a68cf4b12457"));

        var cursor = codec.Encode(sortKey);

        Assert.DoesNotContain(sortKey, cursor, StringComparison.Ordinal);
        Assert.Equal(sortKey, codec.Decode(cursor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("YQ")]
    public void Decode_RejectsMalformedCursor(string cursor)
    {
        var exception = Assert.Throws<FeedRequestException>(() => codec.Decode(cursor));
        Assert.Equal(400, exception.StatusCode);
    }
}
