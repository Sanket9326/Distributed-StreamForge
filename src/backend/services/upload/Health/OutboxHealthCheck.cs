using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StreamForge.Upload.Api.Data;
using StreamForge.Upload.Api.Options;

namespace StreamForge.Upload.Api.Health;

/// <summary>
/// Reports degraded health when the oldest unpublished outbox event exceeds the configured age.
/// </summary>
public sealed class OutboxHealthCheck(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider) : IHealthCheck
{
    private readonly OutboxOptions outboxOptions = options.Value;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<UploadDbContext>();
            var pending = await dbContext.OutboxMessages
                .Where(message => message.ProcessedAtUtc == null)
                .OrderBy(message => message.OccurredAtUtc)
                .Select(message => (DateTimeOffset?)message.OccurredAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (pending is null)
            {
                return HealthCheckResult.Healthy("The outbox is empty.");
            }

            var age = timeProvider.GetUtcNow() - pending.Value;
            return age > TimeSpan.FromSeconds(outboxOptions.DegradedAfterSeconds)
                ? HealthCheckResult.Degraded($"The oldest outbox event is {age:g} old.")
                : HealthCheckResult.Healthy("Outbox events are awaiting publication.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("The outbox could not be inspected.", exception);
        }
    }
}
