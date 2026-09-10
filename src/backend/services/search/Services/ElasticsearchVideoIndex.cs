using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using Microsoft.Extensions.Options;
using StreamForge.Search.Api.Models;
using StreamForge.Search.Api.Options;

namespace StreamForge.Search.Api.Services;

public sealed class ElasticsearchVideoIndex(
    ElasticsearchClient client,
    IOptions<ElasticsearchOptions> options,
    ILogger<ElasticsearchVideoIndex> logger) : IVideoSearchIndex
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ElasticsearchOptions settings = options.Value;

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        var exists = await client.Transport.HeadAsync(
            $"/{Uri.EscapeDataString(settings.IndexName)}",
            cancellationToken);
        if (exists.ApiCallDetails.HttpStatusCode == (int)HttpStatusCode.NotFound)
        {
            var response = await client.Transport.PutAsync<ElasticsearchStringResponse>(
                $"/{Uri.EscapeDataString(settings.IndexName)}",
                PostData.String(BuildCreateIndexPayload(settings.ReadAlias, settings.WriteAlias)),
                cancellationToken);
            EnsureValid(response, "create the search index");
            return;
        }

        if (!exists.ApiCallDetails.HasSuccessfulStatusCode)
        {
            throw new InvalidOperationException($"Elasticsearch index check failed: {exists.ApiCallDetails.DebugInformation}");
        }

        var aliases = await client.Transport.PostAsync<ElasticsearchStringResponse>(
            "/_aliases",
            PostData.String(BuildAliasPayload(settings.IndexName, settings.ReadAlias, settings.WriteAlias)),
            cancellationToken);
        EnsureValid(aliases, "ensure search aliases");
    }

    public async Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        var response = await client.Transport.GetAsync<ElasticsearchStringResponse>(
            $"/_cluster/health/{Uri.EscapeDataString(settings.IndexName)}",
            cancellationToken);
        EnsureValid(response, "verify the search index");
    }

    public async Task<IndexWriteResult> IndexAsync(
        VideoSearchIndexRequestedV1 requested,
        CancellationToken cancellationToken)
    {
        var document = ToDocument(requested);
        ElasticsearchStringResponse response;
        try
        {
            var path = $"/{Uri.EscapeDataString(settings.WriteAlias)}/_doc/{requested.VideoId:D}" +
                $"?version={requested.Revision}&version_type=external_gte";
            response = await client.Transport.PutAsync<ElasticsearchStringResponse>(
                path,
                PostData.String(JsonSerializer.Serialize(document, SerializerOptions)),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Elasticsearch write failed for video {VideoId}", requested.VideoId);
            return IndexWriteResult.TransientFailure;
        }

        var statusCode = response.ApiCallDetails.HttpStatusCode;
        if (response.IsValidResponse)
        {
            return IndexWriteResult.Indexed;
        }

        if (statusCode == (int)HttpStatusCode.Conflict)
        {
            logger.LogInformation(
                "Ignored superseded search revision {Revision} for video {VideoId}",
                requested.Revision,
                requested.VideoId);
            return IndexWriteResult.Superseded;
        }

        logger.LogWarning(
            "Elasticsearch rejected video {VideoId} revision {Revision} with status {StatusCode}: {Error}",
            requested.VideoId,
            requested.Revision,
            statusCode,
            response.ElasticsearchServerError?.ToString() ?? response.DebugInformation);
        return IsTransientStatus(statusCode)
            ? IndexWriteResult.TransientFailure
            : IndexWriteResult.PermanentFailure;
    }

    public async Task<IReadOnlyList<VideoSuggestion>> SuggestAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ElasticsearchStringResponse response;
        try
        {
            response = await client.Transport.PostAsync<ElasticsearchStringResponse>(
                $"/{Uri.EscapeDataString(settings.ReadAlias)}/_search",
                PostData.String(BuildSearchPayload(query, limit)),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SearchUnavailableException("Elasticsearch is unavailable.", exception);
        }

        if (!response.IsValidResponse)
        {
            throw new SearchUnavailableException(
                $"Elasticsearch search failed with status {response.ApiCallDetails.HttpStatusCode}.");
        }

        var result = JsonSerializer.Deserialize<SearchResponseEnvelope>(response.Body, SerializerOptions);
        return result?.Hits.Hits.Select(hit => new VideoSuggestion(
            hit.Source.VideoId,
            hit.Source.Title,
            hit.Source.Description,
            hit.Source.Hashtags)).ToArray() ?? [];
    }

    public static VideoSearchDocument ToDocument(VideoSearchIndexRequestedV1 requested)
    {
        var hashtags = requested.Hashtags
            .Select(tag => tag.Trim().TrimStart('#').ToLowerInvariant())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new VideoSearchDocument(
            requested.VideoId.ToString("D"),
            requested.OwnerId?.ToString("D"),
            requested.Title.Trim(),
            string.IsNullOrWhiteSpace(requested.Description) ? null : requested.Description.Trim(),
            hashtags,
            string.Join(' ', hashtags),
            requested.Revision,
            requested.UploadedAtUtc,
            requested.AvailableAtUtc);
    }

    public static string BuildSearchPayload(string query, int limit) => JsonSerializer.Serialize(new
    {
        size = limit,
        _source = new[] { "videoId", "title", "description", "hashtags" },
        query = new
        {
            multi_match = new
            {
                query,
                type = "bool_prefix",
                fields = new[]
                {
                    "title^4", "title._2gram^4", "title._3gram^4",
                    "hashtagsText^2", "hashtagsText._2gram^2", "hashtagsText._3gram^2",
                    "description"
                }
            }
        }
    }, SerializerOptions);

    public static bool IsTransientStatus(int? statusCode) =>
        statusCode is null or 408 or 429 || statusCode >= 500;

    private static string BuildCreateIndexPayload(string readAlias, string writeAlias) =>
        JsonSerializer.Serialize(new
        {
            settings = new { number_of_shards = 1, number_of_replicas = 0 },
            mappings = new
            {
                dynamic = "strict",
                properties = new Dictionary<string, object>
                {
                    ["videoId"] = new { type = "keyword" },
                    ["ownerId"] = new { type = "keyword" },
                    ["title"] = new { type = "search_as_you_type" },
                    ["description"] = new { type = "text" },
                    ["hashtags"] = new { type = "keyword" },
                    ["hashtagsText"] = new { type = "search_as_you_type" },
                    ["revision"] = new { type = "long" },
                    ["uploadedAtUtc"] = new { type = "date" },
                    ["availableAtUtc"] = new { type = "date" }
                }
            },
            aliases = new Dictionary<string, object>
            {
                [readAlias] = new { },
                [writeAlias] = new { is_write_index = true }
            }
        }, SerializerOptions);

    private static string BuildAliasPayload(string indexName, string readAlias, string writeAlias) =>
        JsonSerializer.Serialize(new
        {
            actions = new object[]
            {
                new { add = new { index = indexName, alias = readAlias } },
                new { add = new { index = indexName, alias = writeAlias, is_write_index = true } }
            }
        }, SerializerOptions);

    private static void EnsureValid(ElasticsearchStringResponse response, string operation)
    {
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException(
                $"Failed to {operation}: {response.ElasticsearchServerError?.ToString() ?? response.DebugInformation}");
        }
    }

    private sealed record SearchResponseEnvelope(SearchHits Hits);
    private sealed record SearchHits(IReadOnlyList<SearchHit> Hits);
    private sealed record SearchHit([property: JsonPropertyName("_source")] VideoSuggestionSource Source);
    private sealed record VideoSuggestionSource(
        string VideoId,
        string Title,
        string? Description,
        IReadOnlyList<string> Hashtags);
}

public sealed class SearchUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
