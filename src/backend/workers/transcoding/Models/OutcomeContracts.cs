namespace StreamForge.Transcoding.Worker.Models;

/// <summary>Identifies a generated rendition stored in MinIO.</summary>
public sealed record RenditionV1(
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

/// <summary>Published after all selected renditions are durable and verified.</summary>
public sealed record VideoTranscodingCompletedV1(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    Guid CausationEventId,
    Guid VideoId,
    string SourceBucket,
    string SourceObjectKey,
    string SourceEtag,
    IReadOnlyList<RenditionV1> Renditions,
    string CorrelationId)
{
    public const string Type = "video.transcoding.completed";

    public const int Version = 1;
}

public sealed record HlsVariantV2(
    string Tier, int Width, int Height, double FrameRate, string VideoCodec, string? AudioCodec,
    string Codecs, long BandwidthBitsPerSecond, long AverageBandwidthBitsPerSecond,
    string PlaylistObjectKey, string PlaylistEtag, int SegmentCount, long SizeBytes);

public sealed record HlsPackageV2(
    string Bucket, string AssetPrefix, string MasterPlaylistObjectKey, string MasterPlaylistEtag,
    string SegmentFormat, int TargetSegmentDurationSeconds, double DurationSeconds, long TotalSizeBytes,
    IReadOnlyList<HlsVariantV2> Variants);

public sealed record VideoTranscodingCompletedV2(
    Guid EventId, string EventType, int EventVersion, DateTimeOffset OccurredAtUtc, Guid CausationEventId,
    Guid VideoId, string SourceBucket, string SourceObjectKey, string SourceEtag,
    IReadOnlyList<RenditionV1> Renditions, HlsPackageV2 HlsPackage, string CorrelationId)
{
    public const string Type = "video.transcoding.completed";
    public const int Version = 2;
}

/// <summary>Published when a valid upload event reaches a terminal processing failure.</summary>
public sealed record VideoTranscodingFailedV1(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    Guid CausationEventId,
    Guid VideoId,
    string FailureCode,
    string FailureReason,
    int AttemptCount,
    string CorrelationId)
{
    public const string Type = "video.transcoding.failed";

    public const int Version = 1;
}

/// <summary>Captures a Kafka envelope that cannot be converted into a valid upload job.</summary>
public sealed record VideoProcessingDeadLetterV1(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceTopic,
    int SourcePartition,
    long SourceOffset,
    string? SourceKey,
    string RejectionCode,
    string RejectionReason,
    string OriginalPayload)
{
    public const string Type = "video.processing.rejected";

    public const int Version = 1;
}
