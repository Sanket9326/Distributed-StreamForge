namespace StreamForge.Transcoding.Worker.Options;

/// <summary>Configures the external FFmpeg executables.</summary>
public sealed class MediaToolOptions
{
    public const string SectionName = "MediaTools";

    public string FfmpegPath { get; init; } = "ffmpeg";

    public string FfprobePath { get; init; } = "ffprobe";
}
