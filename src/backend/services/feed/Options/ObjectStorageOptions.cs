namespace StreamForge.Feed.Api.Options;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string Endpoint { get; init; } = string.Empty;

    public string PublicEndpoint { get; init; } = string.Empty;

    public string AccessKey { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public string RenditionsBucket { get; init; } = "streamforge-renditions";

    public bool UseSsl { get; init; }

    public bool PublicUseSsl { get; init; }

    public int SignedUrlExpirySeconds { get; init; } = 3600;
}
