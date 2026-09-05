using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using StreamForge.Upload.Api.Data;
using StreamForge.Upload.Api.Data.Entities;
using StreamForge.Upload.Api.Models;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.IntegrationTests;

public sealed class UploadsEndpointTests(UploadApiFactory factory) : IClassFixture<UploadApiFactory>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Upload_StoresObjectMetadataAndPublishesProcessingEvent()
    {
        using var consumer = CreateConsumer();
        consumer.Assign(new TopicPartitionOffset(
            UploadApiFactory.TopicName,
            new Partition(0),
            Offset.Beginning));
        using var client = factory.CreateClient();
        const string correlationId = "integration-test-correlation";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        using var response = await client.PostAsync(
            "/api/uploads",
            CreateUpload(
                "source.mp4",
                "video/mp4",
                [1, 2, 3, 4],
                "  Source title  ",
                " Description ",
                ["#DotNet", "video", "dotnet"]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-ID").Single());

        var receipt = await response.Content.ReadFromJsonAsync<UploadResponse>();
        Assert.NotNull(receipt);
        Assert.Equal("Source title", receipt.Title);
        Assert.Equal("Description", receipt.Description);
        Assert.Equal(["dotnet", "video"], receipt.Hashtags);
        Assert.Equal(VideoStatuses.Queued, receipt.Status);
        Assert.Equal("source.mp4", receipt.FileName);
        Assert.Equal(4, receipt.SizeBytes);
        Assert.Equal(correlationId, receipt.CorrelationId);

        var video = await GetVideoAsync(receipt.Id);
        Assert.Equal(UploadApiFactory.OwnerId, video.OwnerId);
        Assert.Equal("streamforge-videos", video.StorageBucket);
        Assert.Matches(
            $"^sources/[0-9]{{4}}/[0-9]{{2}}/[0-9]{{2}}/[0-9]{{8}}T[0-9]{{9}}Z-{receipt.Id:N}\\.mp4$",
            video.StorageObjectKey);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await ReadObjectAsync(video.StorageObjectKey));
        var objectStat = await StatObjectAsync(video.StorageObjectKey);
        Assert.Equal(video.StorageEtag, objectStat.ETag);
        Assert.Equal("video/mp4", objectStat.ContentType);
        Assert.Equal(receipt.Id.ToString("D"), objectStat.MetaData["video-id"]);
        Assert.Equal(correlationId, objectStat.MetaData["correlation-id"]);
        Assert.Equal("source.mp4", objectStat.MetaData["original-file-name"]);
        Assert.Equal(UploadApiFactory.OwnerId.ToString("D"), objectStat.MetaData["owner-id"]);
        Assert.DoesNotContain("title", objectStat.MetaData.Keys);

        await WaitForOutboxProcessedAsync(receipt.Id, TimeSpan.FromSeconds(15));
        var videoUploaded = await ConsumeEventAsync(consumer, receipt.Id, TimeSpan.FromSeconds(15));
        Assert.Equal(VideoUploadedV1.Type, videoUploaded.EventType);
        Assert.Equal(VideoUploadedV1.Version, videoUploaded.EventVersion);
        Assert.Equal(receipt.Id, videoUploaded.VideoId);
        Assert.Equal(video.StorageObjectKey, videoUploaded.ObjectKey);
        Assert.Equal(["dotnet", "video"], videoUploaded.Hashtags);
        Assert.Equal(UploadApiFactory.OwnerId, videoUploaded.OwnerId);
        Assert.Equal(correlationId, videoUploaded.CorrelationId);

        await AssertOutboxProcessedAsync(videoUploaded.EventId);
    }

    [Fact]
    public async Task Upload_UsesUniqueObjectsForDuplicateClientNames()
    {
        using var client = factory.CreateClient();
        using var firstResponse = await client.PostAsync(
            "/api/uploads",
            CreateUpload("duplicate.webm", "video/webm", [1], "First"));
        using var secondResponse = await client.PostAsync(
            "/api/uploads",
            CreateUpload("duplicate.webm", "video/webm", [2], "Second"));

        var first = await firstResponse.Content.ReadFromJsonAsync<UploadResponse>();
        var second = await secondResponse.Content.ReadFromJsonAsync<UploadResponse>();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Id, second.Id);

        var firstVideo = await GetVideoAsync(first.Id);
        var secondVideo = await GetVideoAsync(second.Id);
        Assert.NotEqual(firstVideo.StorageObjectKey, secondVideo.StorageObjectKey);
        Assert.Equal(new byte[] { 1 }, await ReadObjectAsync(firstVideo.StorageObjectKey));
        Assert.Equal(new byte[] { 2 }, await ReadObjectAsync(secondVideo.StorageObjectKey));
    }

    [Fact]
    public async Task Upload_RejectsOversizedVideoWithoutPersistingMetadata()
    {
        using var client = factory.CreateClient();
        var before = await CountVideosAsync();
        var objectsBefore = await CountObjectsAsync();

        using var response = await client.PostAsync(
            "/api/uploads",
            CreateUpload("large.mkv", "video/x-matroska", new byte[9], "Large"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Video is too large", problem?.Title);
        Assert.Equal(before, await CountVideosAsync());
        Assert.Equal(objectsBefore, await CountObjectsAsync());
    }

    [Theory]
    [InlineData("source.txt", "video/mp4", "Title", "tag", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("source.mp4", "text/plain", "Title", "tag", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("source.mp4", "video/mp4", "", "tag", HttpStatusCode.BadRequest)]
    [InlineData("source.mp4", "video/mp4", "Title", "not valid", HttpStatusCode.BadRequest)]
    public async Task Upload_RejectsInvalidFileOrMetadata(
        string fileName,
        string contentType,
        string title,
        string hashtag,
        HttpStatusCode expectedStatus)
    {
        using var client = factory.CreateClient();
        var before = await CountVideosAsync();
        var objectsBefore = await CountObjectsAsync();

        using var response = await client.PostAsync(
            "/api/uploads",
            CreateUpload(fileName, contentType, [1], title, hashtags: [hashtag]));

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, await CountVideosAsync());
        Assert.Equal(objectsBefore, await CountObjectsAsync());
    }

    [Fact]
    public async Task Upload_RequiresTitleAndFileFields()
    {
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Title"), "title");

        using var response = await client.PostAsync("/api/uploads", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsMultipleFiles()
    {
        using var client = factory.CreateClient();
        var objectsBefore = await CountObjectsAsync();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Title"), "title");
        content.Add(CreateFile([1], "video/mp4"), "file", "first.mp4");
        content.Add(CreateFile([2], "video/mp4"), "file", "second.mp4");

        using var response = await client.PostAsync("/api/uploads", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(objectsBefore, await CountObjectsAsync());
    }

    [Fact]
    public async Task Upload_PostgresFailureCompensatesCreatedObject()
    {
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);
        var videosBefore = await CountVideosAsync();
        var objectsBefore = await CountObjectsAsync();

        await factory.PausePostgresAsync();
        HttpResponseMessage? response = null;
        try
        {
            response = await client.PostAsync(
                "/api/uploads",
                CreateUpload("database-failure.mp4", "video/mp4", [1], "Database failure"));
        }
        finally
        {
            await factory.UnpausePostgresAsync();
        }

        using (response)
        {
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        Assert.Equal(objectsBefore, await CountObjectsAsync());
        Assert.Equal(videosBefore, await CountVideosAsync());
    }

    [Fact]
    public async Task Upload_KafkaDowntimeLeavesPendingOutboxThenPublishesAfterRecovery()
    {
        using var client = factory.CreateClient();
        UploadResponse? receipt = null;

        await factory.PauseKafkaAsync();
        try
        {
            using var response = await client.PostAsync(
                "/api/uploads",
                CreateUpload("kafka-recovery.mp4", "video/mp4", [1], "Kafka recovery"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            receipt = await response.Content.ReadFromJsonAsync<UploadResponse>();
            Assert.NotNull(receipt);

            await using var dbContext = CreateDbContext();
            var processedAt = await dbContext.OutboxMessages
                .Where(message => message.VideoId == receipt.Id)
                .Select(message => message.ProcessedAtUtc)
                .SingleAsync();
            Assert.Null(processedAt);
        }
        finally
        {
            await factory.UnpauseKafkaAsync();
        }

        Assert.NotNull(receipt);
        await WaitForOutboxProcessedAsync(receipt.Id, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Initialization_IsIdempotentForMigrationsBucketAndExistingTopic()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UploadDbContext>();
        var objectStorage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var topicManager = scope.ServiceProvider.GetRequiredService<KafkaTopicManager>();

        await dbContext.Database.MigrateAsync();
        await dbContext.Database.MigrateAsync();
        await objectStorage.EnsureBucketAsync(CancellationToken.None);
        await objectStorage.EnsureBucketAsync(CancellationToken.None);
        await topicManager.EnsureTopicAsync(CancellationToken.None);
        await topicManager.EnsureTopicAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Health_ReportsInitializedDependencies()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private IConsumer<Ignore, string> CreateConsumer() =>
        new ConsumerBuilder<Ignore, string>(new ConsumerConfig
        {
            BootstrapServers = factory.KafkaBootstrapServers,
            GroupId = $"streamforge-tests-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();

    private static async Task<VideoUploadedV1> ConsumeEventAsync(
        IConsumer<Ignore, string> consumer,
        Guid videoId,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var consumed = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (consumed is null)
            {
                continue;
            }

            var videoUploaded = JsonSerializer.Deserialize<VideoUploadedV1>(
                consumed.Message.Value,
                SerializerOptions);
            if (videoUploaded?.VideoId == videoId)
            {
                return videoUploaded;
            }
        }

        throw new TimeoutException($"Kafka did not publish an event for video {videoId}.");
    }

    private async Task<VideoRecord> GetVideoAsync(Guid videoId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Videos.AsNoTracking().SingleAsync(video => video.Id == videoId);
    }

    private async Task<int> CountVideosAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Videos.CountAsync();
    }

    private async Task WaitForOutboxProcessedAsync(Guid videoId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var dbContext = CreateDbContext();
            var processed = await dbContext.OutboxMessages
                .AsNoTracking()
                .Where(message => message.VideoId == videoId)
                .Select(message => message.ProcessedAtUtc)
                .SingleAsync();
            if (processed is not null)
            {
                return;
            }

            await Task.Delay(100);
        }

        await using var finalContext = CreateDbContext();
        var pending = await finalContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.VideoId == videoId)
            .Select(message => new { message.AttemptCount, message.LastError })
            .SingleAsync();
        throw new TimeoutException(
            $"The outbox event for video {videoId} was not marked as processed after " +
            $"{pending.AttemptCount} attempts. Last error: {pending.LastError}");
    }

    private async Task AssertOutboxProcessedAsync(Guid eventId)
    {
        await using var dbContext = CreateDbContext();
        var processedAt = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.Id == eventId)
            .Select(message => message.ProcessedAtUtc)
            .SingleAsync();
        Assert.NotNull(processedAt);
    }

    private UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseNpgsql(factory.PostgresConnectionString)
            .Options;
        return new UploadDbContext(options);
    }

    private async Task<byte[]> ReadObjectAsync(string objectKey)
    {
        using var minioClient = CreateMinioClient();
        using var output = new MemoryStream();
        await minioClient.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(UploadApiFactory.BucketName)
                .WithObject(objectKey)
                .WithCallbackStream((stream, cancellationToken) =>
                    stream.CopyToAsync(output, cancellationToken)),
            CancellationToken.None);
        return output.ToArray();
    }

    private async Task<ObjectStat> StatObjectAsync(string objectKey)
    {
        using var minioClient = CreateMinioClient();
        return await minioClient.StatObjectAsync(
            new StatObjectArgs()
                .WithBucket(UploadApiFactory.BucketName)
                .WithObject(objectKey),
            CancellationToken.None);
    }

    private async Task<int> CountObjectsAsync()
    {
        using var minioClient = CreateMinioClient();
        var count = 0;
        await foreach (var _ in minioClient.ListObjectsEnumAsync(
                           new ListObjectsArgs()
                               .WithBucket(UploadApiFactory.BucketName)
                               .WithRecursive(true),
                           CancellationToken.None))
        {
            count++;
        }

        return count;
    }

    private IMinioClient CreateMinioClient() =>
        new MinioClient()
            .WithEndpoint(factory.MinioEndpoint)
            .WithCredentials(UploadApiFactory.MinioAccessKey, UploadApiFactory.MinioSecretKey)
            .WithSSL(false)
            .Build();

    private static MultipartFormDataContent CreateUpload(
        string fileName,
        string contentType,
        byte[] bytes,
        string title,
        string? description = null,
        IReadOnlyList<string>? hashtags = null)
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(title), "title");
        if (description is not null)
        {
            multipart.Add(new StringContent(description), "description");
        }

        foreach (var hashtag in hashtags ?? [])
        {
            multipart.Add(new StringContent(hashtag), "hashtags");
        }

        multipart.Add(CreateFile(bytes, contentType), "file", fileName);
        return multipart;
    }

    private static ByteArrayContent CreateFile(byte[] bytes, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return file;
    }
}
