using StreamForge.Transcoding.Worker.Media;

namespace StreamForge.Transcoding.UnitTests;

public sealed class GeneratedMediaValidatorTests
{
    private static readonly MediaInfo Source =
        new(1920, 1080, "h264", true, "aac", TimeSpan.FromMinutes(1));

    private static readonly RenditionDefinition Expected =
        new("480p", 854, 480, 23, "1500k", "3000k", "96k");

    private readonly GeneratedMediaValidator validator = new();

    [Fact]
    public void Validate_OnePixelDisplayAspectRounding_AcceptsGeneratedMedia()
    {
        var generated = new MediaInfo(853, 480, "h264", true, "aac", TimeSpan.FromMinutes(1));

        validator.Validate(generated, Source, Expected);
    }

    [Fact]
    public void Validate_DimensionsOutsideTolerance_RejectsGeneratedMedia()
    {
        var generated = new MediaInfo(852, 480, "h264", true, "aac", TimeSpan.FromMinutes(1));

        var exception = Assert.Throws<PermanentTranscodingException>(() =>
            validator.Validate(generated, Source, Expected));

        Assert.Equal("generated_media_invalid", exception.Code);
        Assert.Contains("Expected approximately 854x480", exception.SafeMessage, StringComparison.Ordinal);
        Assert.Contains("detected 852x480", exception.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WrongCodec_RejectsGeneratedMedia()
    {
        var generated = new MediaInfo(854, 480, "hevc", true, "aac", TimeSpan.FromMinutes(1));

        var exception = Assert.Throws<PermanentTranscodingException>(() =>
            validator.Validate(generated, Source, Expected));

        Assert.Equal("generated_media_invalid", exception.Code);
    }
}
