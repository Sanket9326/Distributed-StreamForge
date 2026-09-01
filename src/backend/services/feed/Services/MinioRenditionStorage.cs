using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using StreamForge.Feed.Api.Options;

namespace StreamForge.Feed.Api.Services;

public sealed class MinioRenditionStorage : IRenditionStorage
{
    private readonly IMinioClient client;
    private readonly ObjectStorageOptions options;

    public MinioRenditionStorage(IOptions<ObjectStorageOptions> options)
    {
        this.options = options.Value;
        client = new MinioClient()
            .WithEndpoint(this.options.Endpoint)
            .WithCredentials(this.options.AccessKey, this.options.SecretKey)
            .WithSSL(this.options.UseSsl)
            .Build();
    }

    public async Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        var exists = await client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(options.RenditionsBucket),
            cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException(
                $"Rendition bucket '{options.RenditionsBucket}' is unavailable.");
        }
    }
}
