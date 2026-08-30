using StreamForge.Transcoding.Worker.Media;

namespace StreamForge.Transcoding.UnitTests;

public sealed class RenditionSelectorTests
{
    private readonly RenditionSelector selector = new();

    [Fact]
    public void Select_1080pSource_CreatesStandardNonUpscaledLadder()
    {
        var renditions = selector.Select(new MediaInfo(1920, 1080, "h264", true, "aac", TimeSpan.FromMinutes(1)));

        Assert.Collection(
            renditions,
            rendition => AssertRendition(rendition, "480p", 854, 480, 23, "1500k"),
            rendition => AssertRendition(rendition, "720p", 1280, 720, 22, "3000k"),
            rendition => AssertRendition(rendition, "1080p", 1920, 1080, 21, "6000k"));
    }

    [Fact]
    public void Select_720pSource_DoesNotUpscaleTo1080p()
    {
        var renditions = selector.Select(new MediaInfo(1280, 720, "vp9", false, null, TimeSpan.FromSeconds(10)));

        Assert.Equal(["480p", "720p"], renditions.Select(rendition => rendition.Tier));
    }

    [Fact]
    public void Select_SourceBelow480p_CreatesOneEvenSourceSizedRendition()
    {
        var renditions = selector.Select(new MediaInfo(641, 359, "h264", true, "aac", TimeSpan.FromSeconds(10)));

        var rendition = Assert.Single(renditions);
        Assert.Equal("358p", rendition.Tier);
        Assert.Equal(640, rendition.Width);
        Assert.Equal(358, rendition.Height);
    }

    private static void AssertRendition(
        RenditionDefinition actual,
        string tier,
        int width,
        int height,
        int crf,
        string maximumRate)
    {
        Assert.Equal(tier, actual.Tier);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
        Assert.Equal(crf, actual.Crf);
        Assert.Equal(maximumRate, actual.MaximumVideoRate);
    }
}
