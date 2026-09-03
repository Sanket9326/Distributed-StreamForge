using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Media;

public sealed class FfmpegHlsPackager(
    IProcessRunner processRunner,
    IOptions<MediaToolOptions> mediaToolOptions,
    IOptions<TranscodingOptions> transcodingOptions) : IHlsPackager
{
    private readonly MediaToolOptions tools = mediaToolOptions.Value;
    private readonly TranscodingOptions options = transcodingOptions.Value;

    public async Task PackageAsync(string inputPath, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = await processRunner.RunAsync(tools.FfmpegPath,
            BuildArguments(inputPath, Path.Combine(outputDirectory, "index.m3u8"), options.HlsSegmentDurationSeconds),
            TimeSpan.FromSeconds(options.JobTimeoutSeconds), cancellationToken);
        if (result.ExitCode != 0)
            throw new TransientTranscodingException("hls_packaging_failed", "FFmpeg failed while packaging HLS.");
    }

    public static IReadOnlyList<string> BuildArguments(string inputPath, string playlistPath, int segmentSeconds)
    {
        var directory = Path.GetDirectoryName(playlistPath)!;
        return ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", inputPath,
            "-map", "0:v:0", "-map", "0:a:0?", "-c", "copy", "-f", "hls",
            "-hls_time", segmentSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-hls_playlist_type", "vod", "-hls_segment_type", "fmp4",
            "-hls_fmp4_init_filename", "init.mp4", "-hls_segment_filename", Path.Combine(directory, "segment-%05d.m4s"),
            "-hls_flags", "independent_segments", playlistPath];
    }
}
