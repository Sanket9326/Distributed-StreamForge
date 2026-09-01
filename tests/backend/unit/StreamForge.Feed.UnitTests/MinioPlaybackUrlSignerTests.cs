using Microsoft.Extensions.Options;
using StreamForge.Feed.Api.Data.Entities;
using StreamForge.Feed.Api.Options;
using StreamForge.Feed.Api.Services;

namespace StreamForge.Feed.UnitTests;

public sealed class MinioPlaybackUrlSignerTests
{
    [Fact]
    public async Task SignAsync_UsesBrowserVisibleEndpointAndConfiguredExpiry()
    {
        var options = new ObjectStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = "localhost:9000",
            AccessKey = "streamforge-local",
            SecretKey = "integration-secret-key",
            SignedUrlExpirySeconds = 3600
        };
        var signer = new MinioPlaybackUrlSigner(Options.Create(options), TimeProvider.System);
        var rendition = new FeedRendition
        {
            VideoId = Guid.NewGuid(),
            Tier = "1080p",
            Bucket = options.RenditionsBucket,
            ObjectKey = "videos/example/1080p/example.mp4"
        };

        var signed = await signer.SignAsync(rendition, CancellationToken.None);

        Assert.StartsWith(
            "http://localhost:9000/streamforge-renditions/videos/example/1080p/example.mp4?",
            signed.Url,
            StringComparison.Ordinal);
        Assert.Contains("X-Amz-Signature=", signed.Url, StringComparison.Ordinal);
        Assert.InRange(
            signed.ExpiresAtUtc,
            DateTimeOffset.UtcNow.AddMinutes(59),
            DateTimeOffset.UtcNow.AddMinutes(61));
    }
}
