namespace StreamForge.Search.Api.Options;

public sealed class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    public string Endpoint { get; init; } = string.Empty;
    public string IndexName { get; init; } = "streamforge-videos-v1";
    public string ReadAlias { get; init; } = "streamforge-videos-read";
    public string WriteAlias { get; init; } = "streamforge-videos-write";
    public int RequestTimeoutSeconds { get; init; } = 10;
}
