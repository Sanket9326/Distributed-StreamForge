using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using StreamForge.Feed.Api.Data;
using StreamForge.Feed.Api.Data.Entities;
using StreamForge.Feed.Api.Models;
using StreamForge.Feed.Api.Options;
using StreamForge.Feed.Api.Services;
using Testcontainers.PostgreSql;

namespace StreamForge.Feed.IntegrationTests;

public sealed class FeedProjectionTests : IAsyncLifetime
{
    private const string MinioAccessKey = "streamforge-feed-test";
    private const string MinioSecretKey = "streamforge-feed-test-secret";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.6-alpine")
        .WithDatabase("streamforge")
        .WithUsername("streamforge")
        .WithPassword("integration-password")
        .Build();
    private readonly IFutureDockerImage minioImage;
    private readonly IContainer minio;
    private IDbContextFactory<FeedDbContext> contextFactory = null!;

    public FeedProjectionTests()
    {
        var repositoryRoot = FindRepositoryRoot();
        minioImage = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(Path.Combine(repositoryRoot, "infra", "docker", "minio"))
            .WithDockerfile("Dockerfile")
            .WithName("streamforge/minio-test:release-2025-10-15")
            .WithDeleteIfExists(false)
            .WithImageBuildPolicy(_ => false)
            .WithCleanUp(false)
            .Build();
        minio = new ContainerBuilder(minioImage)
            .WithPortBinding(9000, assignRandomHostPort: true)
            .WithEnvironment("MINIO_ROOT_USER", MinioAccessKey)
            .WithEnvironment("MINIO_ROOT_PASSWORD", MinioSecretKey)
            .WithCommand("server", "/data", "--console-address", ":9001")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
                request.ForPort(9000).ForPath("/minio/health/live")))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), minioImage.CreateAsync());
        await minio.StartAsync();
        var options = new DbContextOptionsBuilder<FeedDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", FeedDbContext.Schema))
            .Options;
        contextFactory = new PooledDbContextFactory<FeedDbContext>(options);
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await minio.DisposeAsync();
        await minioImage.DisposeAsync();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task UploadedAndCompletedEvents_CreatePageableReadyFeedWithEveryRendition()
    {
        var projector = new FeedEventProjector(
            contextFactory,
            Options.Create(new KafkaOptions()),
            Options.Create(new ObjectStorageOptions()),
            TimeProvider.System,
            NullLogger<FeedEventProjector>.Instance);
        var newestId = Guid.NewGuid();
        var olderId = Guid.NewGuid();
        await ProjectVideoAsync(projector, newestId, "Newest", DateTimeOffset.UtcNow);
        await ProjectVideoAsync(projector, olderId, "Older", DateTimeOffset.UtcNow.AddMinutes(-1));
        var query = new FeedQueryService(contextFactory, new FeedCursorCodec(), new FakeSigner());

        var first = await query.GetPageAsync(1, null, CancellationToken.None);
        var second = await query.GetPageAsync(1, first.NextCursor, CancellationToken.None);

        Assert.Equal(newestId, Assert.Single(first.Items).Id);
        Assert.Equal(2, first.Items[0].Renditions.Count);
        Assert.All(first.Items[0].Renditions, rendition =>
            Assert.StartsWith("https://storage.test/", rendition.PlaybackUrl, StringComparison.Ordinal));
        Assert.Equal(olderId, Assert.Single(second.Items).Id);
        Assert.Null(second.NextCursor);
        await AssertSignedRangePlaybackAsync();
    }

    private static async Task ProjectVideoAsync(
        FeedEventProjector projector,
        Guid videoId,
        string title,
        DateTimeOffset availableAtUtc)
    {
        var uploaded = new VideoUploadedV1(
            Guid.NewGuid(), VideoUploadedV1.Type, 1, availableAtUtc.AddMinutes(-2), videoId,
            "streamforge-videos", $"sources/{videoId:N}.mp4", "source-etag", "demo.mp4",
            "video/mp4", 100, title, "Description", ["video"], null,
            availableAtUtc.AddMinutes(-2), "correlation");
        var completed = new VideoTranscodingCompletedV1(
            Guid.NewGuid(), VideoTranscodingCompletedV1.Type, 1, availableAtUtc, Guid.NewGuid(),
            videoId, "streamforge-videos", $"sources/{videoId:N}.mp4", "source-etag",
            [Rendition(videoId, "480p", 854, 480), Rendition(videoId, "1080p", 1920, 1080)],
            "correlation");
        await projector.ProjectAsync(Envelope("video-processing", uploaded), CancellationToken.None);
        await projector.ProjectAsync(
            Envelope("video-transcoding-completed", completed),
            CancellationToken.None);
    }

    private static RenditionV1 Rendition(Guid videoId, string tier, int width, int height) => new(
        tier, width, height, "h264", "aac", "video/mp4", "streamforge-renditions",
        $"videos/{videoId:N}/{tier}/{videoId:N}-{tier}.mp4", "etag", 100);

    private static ConsumedEnvelope Envelope(string topic, object payload) => new(
        topic,
        0,
        Random.Shared.NextInt64(1, long.MaxValue),
        null,
        JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private async Task AssertSignedRangePlaybackAsync()
    {
        var endpoint = $"{minio.Hostname}:{minio.GetMappedPublicPort(9000)}";
        using var storage = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(MinioAccessKey, MinioSecretKey)
            .Build();
        const string bucket = "streamforge-renditions";
        const string objectKey = "videos/range-test/1080p/range-test.mp4";
        await storage.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));
        var bytes = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        await using var content = new MemoryStream(bytes);
        await storage.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithStreamData(content)
            .WithObjectSize(bytes.Length)
            .WithContentType("video/mp4"));
        var signer = new MinioPlaybackUrlSigner(Options.Create(new ObjectStorageOptions
        {
            Endpoint = endpoint,
            PublicEndpoint = endpoint,
            AccessKey = MinioAccessKey,
            SecretKey = MinioSecretKey,
            RenditionsBucket = bucket,
            SignedUrlExpirySeconds = 3600
        }), TimeProvider.System);
        var signed = await signer.SignAsync(new FeedRendition
        {
            VideoId = Guid.NewGuid(),
            Tier = "1080p",
            Bucket = bucket,
            ObjectKey = objectKey
        }, CancellationToken.None);

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, signed.Url);
        request.Headers.Range = new RangeHeaderValue(2, 5);
        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes 2-5/32", response.Content.Headers.ContentRange?.ToString());
        Assert.Equal([2, 3, 4, 5], await response.Content.ReadAsByteArrayAsync());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "StreamForge.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Could not find the StreamForge repository root.");
    }

    private sealed class FakeSigner : IPlaybackUrlSigner
    {
        public Task<SignedPlaybackUrl> SignAsync(
            FeedRendition rendition,
            CancellationToken cancellationToken) => Task.FromResult(new SignedPlaybackUrl(
                $"https://storage.test/{rendition.ObjectKey}",
                DateTimeOffset.UtcNow.AddHours(1)));
    }
}
