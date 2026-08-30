namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Coordinates durable job leases and terminal outcome transactions.</summary>
public interface IJobStore
{
    Task<LeasedJob?> ClaimNextAsync(string leaseOwner, CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(Guid eventId, string leaseOwner, CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        LeasedJob job,
        IReadOnlyList<ProcessedRendition> renditions,
        CancellationToken cancellationToken);

    Task<bool> ScheduleRetryAsync(
        LeasedJob job,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);

    Task<bool> FailAsync(
        LeasedJob job,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);
}
