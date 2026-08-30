using StreamForge.Transcoding.Worker.Media;

namespace StreamForge.Transcoding.UnitTests;

public sealed class RenditionKeyFactoryTests
{
    [Fact]
    public void Create_UsesVideoIdAndTierWithoutClientFileName()
    {
        var videoId = Guid.Parse("e2c1bb10-4340-452f-9fc6-a68cf4b12457");
        var rendition = new RenditionDefinition("720p", 1280, 720, 22, "3000k", "6000k", "128k");

        var key = new RenditionKeyFactory().Create(videoId, rendition);

        Assert.Equal(
            "videos/e2c1bb104340452f9fc6a68cf4b12457/720p/e2c1bb104340452f9fc6a68cf4b12457-720p.mp4",
            key);
    }
}
