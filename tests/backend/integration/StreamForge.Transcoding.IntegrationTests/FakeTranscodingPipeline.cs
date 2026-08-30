using StreamForge.Transcoding.Worker.Media;
using StreamForge.Transcoding.Worker.Services;

namespace StreamForge.Transcoding.IntegrationTests;

public sealed class FakeTranscodingPipeline : ITranscodingPipeline
{
    public Task<IReadOnlyList<ProcessedRendition>> ProcessAsync(
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
        return Task.FromResult(renditions);
    }
}
