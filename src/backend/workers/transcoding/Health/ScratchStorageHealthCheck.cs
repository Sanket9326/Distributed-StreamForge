using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Health;

public sealed class ScratchStorageHealthCheck(IOptions<TranscodingOptions> options) : IHealthCheck
{
    private readonly TranscodingOptions transcodingOptions = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.GetFullPath(transcodingOptions.ScratchPath);
            Directory.CreateDirectory(fullPath);
            var probePath = Path.Combine(fullPath, $".health-{Guid.NewGuid():N}");
            using (var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose))
            {
                probe.WriteByte(0);
            }
            var root = Path.GetPathRoot(fullPath) ?? fullPath;
            var available = new DriveInfo(root).AvailableFreeSpace;
            return Task.FromResult(available >= transcodingOptions.MinimumFreeScratchBytes
                ? HealthCheckResult.Healthy($"Scratch storage has {available} bytes available.")
                : HealthCheckResult.Unhealthy(
                    $"Scratch storage has {available} bytes; {transcodingOptions.MinimumFreeScratchBytes} are required."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Scratch storage is unavailable.", exception));
        }
    }
}
