namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Turns one leased source job into verified MinIO renditions.</summary>
public interface ITranscodingPipeline
{
    Task<ProcessedTranscodingResult> ProcessAsync(
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

public sealed record ProcessedHlsVariant(
    string Tier, int Width, int Height, double FrameRate, string VideoCodec, string? AudioCodec,
    string Codecs, long BandwidthBitsPerSecond, long AverageBandwidthBitsPerSecond,
    string PlaylistObjectKey, string PlaylistEtag, int SegmentCount, long SizeBytes);

public sealed record ProcessedHlsPackage(
    string Bucket, string AssetPrefix, string MasterPlaylistObjectKey, string MasterPlaylistEtag,
    string SegmentFormat, int TargetSegmentDurationSeconds, double DurationSeconds, long TotalSizeBytes,
    IReadOnlyList<ProcessedHlsVariant> Variants);

public sealed record ProcessedTranscodingResult(
    IReadOnlyList<ProcessedRendition> Renditions,
    ProcessedHlsPackage HlsPackage);
