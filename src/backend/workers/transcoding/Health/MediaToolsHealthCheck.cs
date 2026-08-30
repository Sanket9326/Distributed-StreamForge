using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Media;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Health;

public sealed class MediaToolsHealthCheck(
    IMediaProbe mediaProbe,
    IProcessRunner processRunner,
    IOptions<MediaToolOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await mediaProbe.VerifyAvailableAsync(cancellationToken);
            var ffmpeg = await processRunner.RunAsync(
                options.Value.FfmpegPath,
                ["-version"],
                TimeSpan.FromSeconds(5),
                cancellationToken);
            return ffmpeg.ExitCode == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("FFmpeg did not report a valid version.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("FFmpeg or ffprobe is unavailable.", exception);
        }
    }
}
