using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Media;

/// <summary>Uses the FFmpeg CLI to produce H.264/AAC MP4 renditions.</summary>
public sealed class FfmpegVideoEncoder(
    IProcessRunner processRunner,
    IOptions<MediaToolOptions> mediaToolOptions,
    IOptions<TranscodingOptions> transcodingOptions) : IVideoEncoder
{
    private readonly MediaToolOptions mediaTools = mediaToolOptions.Value;
    private readonly TimeSpan processTimeout = TimeSpan.FromSeconds(transcodingOptions.Value.JobTimeoutSeconds);

    public async Task EncodeAsync(
        string inputPath,
        string outputPath,
        RenditionDefinition rendition,
        bool hasAudio,
        CancellationToken cancellationToken)
    {
        var arguments = BuildArguments(inputPath, outputPath, rendition, hasAudio);
        var result = await processRunner.RunAsync(
            mediaTools.FfmpegPath,
            arguments,
            processTimeout,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new TransientTranscodingException(
                "ffmpeg_failed",
                $"FFmpeg failed while generating {rendition.Tier}.");
        }
    }

    public static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        RenditionDefinition rendition,
        bool hasAudio)
    {
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-i", inputPath,
            "-map", "0:v:0"
        };
        if (hasAudio)
        {
            arguments.AddRange(["-map", "0:a:0?", "-c:a", "aac", "-b:a", rendition.AudioRate]);
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange(
        [
            "-vf", $"scale={rendition.Width}:{rendition.Height}:flags=lanczos",
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", rendition.Crf.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-maxrate", rendition.MaximumVideoRate,
            "-bufsize", rendition.VideoBufferSize,
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            "-map_metadata", "-1",
            "-sn", "-dn",
            outputPath
        ]);
        return arguments;
    }
}
