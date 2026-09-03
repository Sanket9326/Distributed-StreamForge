using StreamForge.Transcoding.Worker.Media;

namespace StreamForge.Transcoding.UnitTests;

public sealed class FfmpegVideoEncoderTests
{
    [Fact]
    public void BuildArguments_WithAudio_UsesRequiredCompatibilityProfile()
    {
        var rendition = new RenditionDefinition("720p", 1280, 720, 22, "3000k", "6000k", "128k");

        var arguments = FfmpegVideoEncoder.BuildArguments(
            "source file.mov",
            "output file.mp4",
            rendition,
            hasAudio: true);

        AssertArgumentPair(arguments, "-c:v", "libx264");
        AssertArgumentPair(arguments, "-preset", "medium");
        AssertArgumentPair(arguments, "-crf", "22");
        AssertArgumentPair(arguments, "-maxrate", "3000k");
        AssertArgumentPair(arguments, "-bufsize", "6000k");
        AssertArgumentPair(arguments, "-pix_fmt", "yuv420p");
        AssertArgumentPair(arguments, "-movflags", "+faststart");
        AssertArgumentPair(arguments, "-c:a", "aac");
        AssertArgumentPair(arguments, "-b:a", "128k");
        Assert.Contains("source file.mov", arguments);
        Assert.Contains("output file.mp4", arguments);
    }

    [Fact]
    public void BuildArguments_WithoutAudio_AddsSilentAacTrack()
    {
        var rendition = new RenditionDefinition("480p", 854, 480, 23, "1500k", "3000k", "96k");

        var arguments = FfmpegVideoEncoder.BuildArguments("source", "output", rendition, hasAudio: false);

        Assert.Contains("anullsrc=channel_layout=stereo:sample_rate=48000", arguments);
        AssertArgumentPair(arguments, "-c:a", "aac");
        AssertArgumentPair(arguments, "-b:a", "96k");
    }

    private static void AssertArgumentPair(IReadOnlyList<string> arguments, string name, string value)
    {
        var index = arguments.IndexOf(name);
        Assert.True(index >= 0, $"Argument {name} was missing.");
        Assert.Equal(value, arguments[index + 1]);
    }
}

file static class ReadOnlyListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return index;
            }
        }

        return -1;
    }
}
