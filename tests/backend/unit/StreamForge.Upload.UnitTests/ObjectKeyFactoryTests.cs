using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.UnitTests;

public sealed class ObjectKeyFactoryTests
{
    [Fact]
    public void Create_UsesUtcTimestampVideoIdAndExtension()
    {
        var videoId = Guid.Parse("fb79f32a-f61e-48a2-a560-eaed870ea40c");
        var uploadedAt = new DateTimeOffset(2026, 8, 29, 10, 30, 45, 123, TimeSpan.Zero);

        var objectKey = new ObjectKeyFactory().Create(videoId, uploadedAt, ".mp4");

        Assert.Equal(
            "sources/2026/08/29/20260829T103045123Z-fb79f32af61e48a2a560eaed870ea40c.mp4",
            objectKey);
    }
}
