using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Transcoding.Worker.Services;

namespace StreamForge.Transcoding.Worker.Health;

public sealed class StartupHealthCheck(StartupGate startupGate) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(startupGate.IsReady
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Infrastructure initialization has not completed."));
}
