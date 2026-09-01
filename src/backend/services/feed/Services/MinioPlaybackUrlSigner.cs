using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using StreamForge.Feed.Api.Data.Entities;
using StreamForge.Feed.Api.Options;

namespace StreamForge.Feed.Api.Services;

public sealed class MinioPlaybackUrlSigner : IPlaybackUrlSigner
{
    private readonly IMinioClient client;
    private readonly ObjectStorageOptions options;
    private readonly TimeProvider timeProvider;

    public MinioPlaybackUrlSigner(
        IOptions<ObjectStorageOptions> options,
        TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.timeProvider = timeProvider;
        client = new MinioClient()
            .WithEndpoint(this.options.PublicEndpoint)
            .WithCredentials(this.options.AccessKey, this.options.SecretKey)
            .WithSSL(this.options.PublicUseSsl)
            .Build();
    }

    public async Task<SignedPlaybackUrl> SignAsync(
        FeedRendition rendition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = await client.PresignedGetObjectAsync(
            new PresignedGetObjectArgs()
                .WithBucket(rendition.Bucket)
                .WithObject(rendition.ObjectKey)
                .WithExpiry(options.SignedUrlExpirySeconds));
        return new SignedPlaybackUrl(
            url,
            timeProvider.GetUtcNow().AddSeconds(options.SignedUrlExpirySeconds));
    }
}
