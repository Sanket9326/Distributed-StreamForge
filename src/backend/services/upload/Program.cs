using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using StreamForge.Upload.Api.Data;
using StreamForge.Upload.Api.Health;
using StreamForge.Upload.Api.Middleware;
using StreamForge.Upload.Api.Options;
using StreamForge.Upload.Api.Services;

const long maximumRequestBodyBytes = 1_074_790_400;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maximumRequestBodyBytes);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UploadExceptionHandler>();
builder.Services.AddControllers();

builder.Services
    .AddOptions<UploadOptions>()
    .BindConfiguration(UploadOptions.SectionName)
    .Validate(options => options.MaxFileSizeBytes > 0, "MaxFileSizeBytes must be greater than zero.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ObjectStorageOptions>()
    .BindConfiguration(ObjectStorageOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Endpoint), "Endpoint is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AccessKey), "AccessKey is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.SecretKey), "SecretKey is required.")
    .Validate(options => options.Bucket.Length is >= 3 and <= 63, "Bucket must contain 3-63 characters.")
    .ValidateOnStart();
builder.Services
    .AddOptions<KafkaOptions>()
    .BindConfiguration(KafkaOptions.SectionName)
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.BootstrapServers),
        "BootstrapServers is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.TopicName), "TopicName is required.")
    .Validate(options => options.PartitionCount > 0, "PartitionCount must be greater than zero.")
    .Validate(options => options.ReplicationFactor > 0, "ReplicationFactor must be greater than zero.")
    .Validate(
        options => options.InitializationTimeoutSeconds > 0,
        "InitializationTimeoutSeconds must be greater than zero.")
    .ValidateOnStart();
builder.Services
    .AddOptions<OutboxOptions>()
    .BindConfiguration(OutboxOptions.SectionName)
    .Validate(
        options => options.PollIntervalMilliseconds > 0,
        "PollIntervalMilliseconds must be greater than zero.")
    .Validate(options => options.BatchSize > 0, "BatchSize must be greater than zero.")
    .Validate(
        options => options.MaximumRetryDelaySeconds > 0,
        "MaximumRetryDelaySeconds must be greater than zero.")
    .Validate(
        options => options.DegradedAfterSeconds > 0,
        "DegradedAfterSeconds must be greater than zero.")
    .ValidateOnStart();

var uploadDatabase = builder.Configuration.GetConnectionString("UploadDatabase");
if (string.IsNullOrWhiteSpace(uploadDatabase))
{
    throw new InvalidOperationException("ConnectionStrings:UploadDatabase is required.");
}

builder.Services.AddDbContext<UploadDbContext>(options =>
    options.UseNpgsql(uploadDatabase, npgsql =>
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(2),
            errorCodesToAdd: null)));
builder.Services.AddSingleton<IMinioClient>(services =>
{
    var options = services.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;
    return new MinioClient()
        .WithEndpoint(options.Endpoint)
        .WithCredentials(options.AccessKey, options.SecretKey)
        .WithSSL(options.UseSsl)
        .Build();
});

builder.Services.AddSingleton<VideoFileValidator>();
builder.Services.AddSingleton<UploadMetadataValidator>();
builder.Services.AddSingleton<ObjectKeyFactory>();
builder.Services.AddSingleton<ObjectMetadataFactory>();
builder.Services.AddSingleton<OutboxMessageFactory>();
builder.Services.AddSingleton<IObjectStorage, MinioObjectStorage>();
builder.Services.AddScoped<VideoSubmissionStore>();
builder.Services.AddScoped<IVideoIngestionService, VideoIngestionService>();
builder.Services.AddSingleton<KafkaTopicManager>();
builder.Services.AddSingleton<IKafkaPublisher, KafkaPublisher>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHostedService<InfrastructureInitializer>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql")
    .AddCheck<ObjectStorageHealthCheck>("minio")
    .AddCheck<KafkaHealthCheck>("kafka")
    .AddCheck<OutboxHealthCheck>("outbox");

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

/// <summary>
/// Exposes the application entry point for integration-test hosting.
/// </summary>
public partial class Program;
