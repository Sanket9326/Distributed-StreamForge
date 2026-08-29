using Microsoft.AspNetCore.Http;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.UnitTests;

public sealed class SizeLimitedReadStreamTests
{
    [Fact]
    public async Task ReadAsync_TracksBytesWithinLimit()
    {
        await using var stream = new SizeLimitedReadStream(
            new MemoryStream([1, 2, 3, 4]),
            maximumBytes: 4);
        await using var destination = new MemoryStream();

        await stream.CopyToAsync(destination);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, destination.ToArray());
        Assert.Equal(4, stream.BytesRead);
    }

    [Fact]
    public async Task ReadAsync_RejectsTheFirstBytePastLimit()
    {
        await using var stream = new SizeLimitedReadStream(
            new MemoryStream([1, 2, 3, 4, 5]),
            maximumBytes: 4);
        await using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<UploadRequestException>(async () =>
            await stream.CopyToAsync(destination));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
        Assert.Equal(5, stream.BytesRead);
    }
}
