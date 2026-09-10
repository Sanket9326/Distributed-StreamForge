using System.Text.Json.Serialization;

namespace StreamForge.Search.Api.Models;

public sealed record VideoSearchIndexRequestedV1(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    Guid CausationEventId,
    string CorrelationId,
    Guid VideoId,
    long Revision,
    Guid? OwnerId,
    string Title,
    string? Description,
    IReadOnlyList<string> Hashtags,
    DateTimeOffset UploadedAtUtc,
    DateTimeOffset AvailableAtUtc)
{
    public const string Type = "video.search.index-requested";
    public const int Version = 1;
}

public sealed record VideoSearchDocument(
    [property: JsonPropertyName("videoId")] string VideoId,
    [property: JsonPropertyName("ownerId")] string? OwnerId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("hashtags")] IReadOnlyList<string> Hashtags,
    [property: JsonPropertyName("hashtagsText")] string HashtagsText,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("uploadedAtUtc")] DateTimeOffset UploadedAtUtc,
    [property: JsonPropertyName("availableAtUtc")] DateTimeOffset AvailableAtUtc);

public sealed record VideoSuggestion(
    string VideoId,
    string Title,
    string? Description,
    IReadOnlyList<string> Hashtags);

public sealed record VideoSuggestionsResponse(IReadOnlyList<VideoSuggestion> Items);

public sealed record SearchIndexDeadLetterV1(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string SourceTopic,
    int SourcePartition,
    long SourceOffset,
    string? SourceKey,
    string Reason,
    string Payload)
{
    public const string Type = "video.search.index-failed";
    public const int Version = 1;
}
