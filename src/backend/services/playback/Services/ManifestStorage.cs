using Microsoft.Extensions.Options;
using Minio; using Minio.DataModel.Args;
using StreamForge.Playback.Api.Options;
namespace StreamForge.Playback.Api.Services;
public interface IManifestStorage { Task<string> ReadAsync(string bucket,string key,CancellationToken token); Task<string> SignAsync(string bucket,string key,CancellationToken token); Task VerifyAsync(CancellationToken token); }
public sealed class ManifestStorage:IManifestStorage
{
    private readonly IMinioClient internalClient; private readonly IMinioClient publicClient; private readonly StorageOptions storage; private readonly PlaybackOptions playback;
    public ManifestStorage(IOptions<StorageOptions> storageOptions,IOptions<PlaybackOptions> playbackOptions){storage=storageOptions.Value;playback=playbackOptions.Value;internalClient=new MinioClient().WithEndpoint(storage.Endpoint).WithCredentials(storage.AccessKey,storage.SecretKey).WithSSL(storage.UseSsl).Build();publicClient=new MinioClient().WithEndpoint(storage.PublicEndpoint).WithCredentials(storage.AccessKey,storage.SecretKey).WithSSL(storage.PublicUseSsl).Build();}
    public async Task<string> ReadAsync(string bucket,string key,CancellationToken token){using var memory=new MemoryStream();await internalClient.GetObjectAsync(new GetObjectArgs().WithBucket(bucket).WithObject(key).WithCallbackStream(stream=>stream.CopyTo(memory)),token);return System.Text.Encoding.UTF8.GetString(memory.ToArray());}
    public async Task<string> SignAsync(string bucket,string key,CancellationToken token){token.ThrowIfCancellationRequested();return await publicClient.PresignedGetObjectAsync(new PresignedGetObjectArgs().WithBucket(bucket).WithObject(key).WithExpiry(playback.SignedUrlExpirySeconds));}
    public async Task VerifyAsync(CancellationToken token){if(!await internalClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(storage.RenditionsBucket),token))throw new InvalidOperationException("Renditions bucket is unavailable.");}
}
