using StreamForge.Transcoding.Worker.Media;
using StreamForge.Transcoding.Worker.Services;

namespace StreamForge.Transcoding.IntegrationTests;

public sealed class FakeTranscodingPipeline : ITranscodingPipeline
{
    public Task<ProcessedTranscodingResult> ProcessAsync(
        LeasedJob job,
        CancellationToken cancellationToken)
    {
        if (job.SourceObjectKey.Contains("invalid", StringComparison.Ordinal))
        {
            throw new PermanentTranscodingException(
                "source_media_invalid",
                "The media file could not be probed.");
        }

        IReadOnlyList<ProcessedRendition> renditions =
        [
            new ProcessedRendition(
                "480p",
                854,
                480,
                "h264",
                "aac",
                "video/mp4",
                "streamforge-renditions",
                $"videos/{job.VideoId:N}/480p/{job.VideoId:N}-480p.mp4",
                "rendition-etag",
                1_024)
        ];
        var prefix = $"videos/{job.VideoId:N}/hls/";
        return Task.FromResult(new ProcessedTranscodingResult(renditions,
            new ProcessedHlsPackage("streamforge-renditions", prefix, prefix + "master.m3u8", "master-etag", "fmp4", 4, 10, 2048,
                [new ProcessedHlsVariant("480p",854,480,30,"h264","aac","avc1.4d401f,mp4a.40.2",1_628_000,1_200_000,prefix+"480p/index.m3u8","playlist-etag",3,2048)])));
    }
}
