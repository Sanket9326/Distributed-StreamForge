namespace StreamForge.Feed.Api.Models;

public sealed record VideoUploadedV1(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    Guid VideoId,
    string Bucket,
    string ObjectKey,
    string Etag,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Title,
    string? Description,
    IReadOnlyList<string> Hashtags,
    Guid? OwnerId,
    DateTimeOffset UploadedAtUtc,
    string CorrelationId)
{
    public const string Type = "video.uploaded";

    public const int Version = 1;
}

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

public sealed record HlsVariantV2(string Tier,int Width,int Height,double FrameRate,string VideoCodec,string? AudioCodec,string Codecs,long BandwidthBitsPerSecond,long AverageBandwidthBitsPerSecond,string PlaylistObjectKey,string PlaylistEtag,int SegmentCount,long SizeBytes);
public sealed record HlsPackageV2(string Bucket,string AssetPrefix,string MasterPlaylistObjectKey,string MasterPlaylistEtag,string SegmentFormat,int TargetSegmentDurationSeconds,double DurationSeconds,long TotalSizeBytes,IReadOnlyList<HlsVariantV2> Variants);
public sealed record VideoTranscodingCompletedV2(Guid EventId,string EventType,int EventVersion,DateTimeOffset OccurredAtUtc,Guid CausationEventId,Guid VideoId,string SourceBucket,string SourceObjectKey,string SourceEtag,IReadOnlyList<RenditionV1> Renditions,HlsPackageV2 HlsPackage,string CorrelationId)
{
    public const string Type="video.transcoding.completed"; public const int Version=2;
}
