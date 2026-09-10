using System.Text.Json;
using StreamForge.Search.Api.Models;

namespace StreamForge.Search.Api.Services;

public static class SearchEventParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static ParsedSearchEvent Parse(string payload)
    {
        try
        {
            var requested = JsonSerializer.Deserialize<VideoSearchIndexRequestedV1>(payload, SerializerOptions);
            if (requested is null ||
                requested.EventId == Guid.Empty ||
                requested.EventType != VideoSearchIndexRequestedV1.Type ||
                requested.EventVersion != VideoSearchIndexRequestedV1.Version ||
                requested.OccurredAtUtc == default ||
                requested.CausationEventId == Guid.Empty ||
                string.IsNullOrWhiteSpace(requested.CorrelationId) ||
                requested.VideoId == Guid.Empty ||
                requested.Revision <= 0 ||
                string.IsNullOrWhiteSpace(requested.Title) ||
                requested.Title.Trim().Length > 200 ||
                (requested.Description?.Trim().Length ?? 0) > 5_000 ||
                requested.Hashtags is null ||
                requested.Hashtags.Count > 10 ||
                requested.Hashtags.Any(hashtag =>
                    string.IsNullOrWhiteSpace(hashtag) || hashtag.Trim().TrimStart('#').Length > 50) ||
                requested.UploadedAtUtc == default ||
                requested.AvailableAtUtc == default)
            {
                return ParsedSearchEvent.Rejected("invalid_contract");
            }

            return new ParsedSearchEvent(requested, null);
        }
        catch (JsonException)
        {
            return ParsedSearchEvent.Rejected("malformed_json");
        }
    }
}

public sealed record ParsedSearchEvent(VideoSearchIndexRequestedV1? Event, string? RejectionReason)
{
    public static ParsedSearchEvent Rejected(string reason) => new(null, reason);
}
