using System.Text.Json;
using StreamForge.Feed.Api.Data.Entities;
using StreamForge.Feed.Api.Models;

namespace StreamForge.Feed.Api.Services;

public static class SearchIndexOutboxFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static OutboxMessage Create(VideoSearchIndexRequestedV1 requested, string topic) => new()
    {
        Id = requested.EventId,
        VideoId = requested.VideoId,
        Revision = requested.Revision,
        Type = requested.EventType,
        Version = requested.EventVersion,
        Topic = topic,
        PartitionKey = requested.VideoId.ToString("D"),
        Payload = JsonSerializer.Serialize(requested, SerializerOptions),
        OccurredAtUtc = requested.OccurredAtUtc,
        NextAttemptAtUtc = requested.OccurredAtUtc
    };
}
