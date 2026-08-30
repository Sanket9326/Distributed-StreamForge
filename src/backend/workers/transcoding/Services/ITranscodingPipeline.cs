namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Turns one leased source job into verified MinIO renditions.</summary>
public interface ITranscodingPipeline
{
    Task<IReadOnlyList<ProcessedRendition>> ProcessAsync(
        LeasedJob job,
        CancellationToken cancellationToken);
}

public sealed record LeasedJob(
    Guid EventId,
    Guid VideoId,
    string SourceBucket,
    string SourceObjectKey,
    string SourceEtag,
    long SourceSizeBytes,
    string OriginalFileName,
    string CorrelationId,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    string LeaseOwner);

public sealed record ProcessedRendition(
    string Tier,
    int Width,
    int Height,
    string VideoCodec,
    string? AudioCodec,
    string ContentType,
    string Bucket,
    string ObjectKey,
    string Etag,
    long SizeBytes);
