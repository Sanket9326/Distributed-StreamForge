namespace StreamForge.Feed.Api.Models;

public sealed record FeedPageResponse(
    IReadOnlyList<FeedVideoResponse> Items,
    string? NextCursor);

public sealed record FeedVideoResponse(
    Guid Id,
    string Title,
    string? Description,
    IReadOnlyList<string> Hashtags,
    DateTimeOffset UploadedAtUtc,
    DateTimeOffset AvailableAtUtc,
    string? HlsManifestUrl,
    IReadOnlyList<FeedRenditionResponse> Renditions);

public sealed record FeedRenditionResponse(
    string Tier,
    int Width,
    int Height,
    string VideoCodec,
    string? AudioCodec,
    string ContentType,
    long SizeBytes,
    string PlaybackUrl,
    DateTimeOffset PlaybackUrlExpiresAtUtc);

public sealed record CompletionEventResponse(
    Guid VideoId,
    DateTimeOffset AvailableAtUtc);
