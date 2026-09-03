using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Minio;
using StreamForge.Transcoding.Worker.Data;
using StreamForge.Transcoding.Worker.Health;
using StreamForge.Transcoding.Worker.Media;
using StreamForge.Transcoding.Worker.Options;
using StreamForge.Transcoding.Worker.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<KafkaOptions>()
    .BindConfiguration(KafkaOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.BootstrapServers), "BootstrapServers is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConsumerGroupId), "ConsumerGroupId is required.")
    .Validate(options => TopicNamesAreValid(options), "Kafka topic names must be non-empty and distinct.")
    .Validate(options => options.PartitionCount > 0, "PartitionCount must be greater than zero.")
    .Validate(options => options.ReplicationFactor > 0, "ReplicationFactor must be greater than zero.")
    .Validate(options => options.InitializationTimeoutSeconds > 0, "InitializationTimeoutSeconds must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<ObjectStorageOptions>()
    .BindConfiguration(ObjectStorageOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Endpoint), "Endpoint is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AccessKey), "AccessKey is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.SecretKey), "SecretKey is required.")
    .Validate(options => options.RenditionsBucket.Length is >= 3 and <= 63, "RenditionsBucket must contain 3-63 characters.")
    .ValidateOnStart();
builder.Services.AddOptions<TranscodingOptions>()
    .BindConfiguration(TranscodingOptions.SectionName)
    .Validate(options => options.MaxConcurrentJobs > 0, "MaxConcurrentJobs must be greater than zero.")
    .Validate(options => options.MaxAttempts > 0, "MaxAttempts must be greater than zero.")
    .Validate(options => options.PollIntervalMilliseconds > 0, "PollIntervalMilliseconds must be greater than zero.")
    .Validate(options => options.LeaseDurationSeconds > options.LeaseHeartbeatSeconds, "LeaseDurationSeconds must exceed LeaseHeartbeatSeconds.")
    .Validate(options => options.LeaseHeartbeatSeconds > 0, "LeaseHeartbeatSeconds must be greater than zero.")
    .Validate(options => options.RetryBaseDelaySeconds > 0, "RetryBaseDelaySeconds must be greater than zero.")
    .Validate(options => options.RetryMaximumDelaySeconds >= options.RetryBaseDelaySeconds, "RetryMaximumDelaySeconds must not be below the base delay.")
    .Validate(options => options.JobTimeoutSeconds > 0, "JobTimeoutSeconds must be greater than zero.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ScratchPath), "ScratchPath is required.")
    .Validate(options => options.MinimumFreeScratchBytes >= 0, "MinimumFreeScratchBytes cannot be negative.")
    .Validate(options => options.HlsSegmentDurationSeconds > 0, "HlsSegmentDurationSeconds must be positive.")
    .Validate(options => options.AssetUploadConcurrency > 0, "AssetUploadConcurrency must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<MediaToolOptions>()
    .BindConfiguration(MediaToolOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.FfmpegPath), "FfmpegPath is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.FfprobePath), "FfprobePath is required.")
    .ValidateOnStart();
builder.Services.AddOptions<OutboxOptions>()
    .BindConfiguration(OutboxOptions.SectionName)
    .Validate(options => options.PollIntervalMilliseconds > 0, "PollIntervalMilliseconds must be greater than zero.")
    .Validate(options => options.BatchSize > 0, "BatchSize must be greater than zero.")
    .Validate(options => options.MaximumRetryDelaySeconds > 0, "MaximumRetryDelaySeconds must be greater than zero.")
    .ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("TranscodingDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:TranscodingDatabase is required.");
}

builder.Services.AddDbContextFactory<TranscodingDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
        npgsql.MigrationsHistoryTable("__ef_migrations_history", TranscodingDbContext.Schema);
    }));
builder.Services.AddSingleton<IMinioClient>(services =>
{
    var options = services.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;
    return new MinioClient()
        .WithEndpoint(options.Endpoint)
        .WithCredentials(options.AccessKey, options.SecretKey)
        .WithSSL(options.UseSsl)
        .Build();
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StartupGate>();
builder.Services.AddSingleton<KafkaTopicManager>();
builder.Services.AddSingleton<IObjectStorage, MinioObjectStorage>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();
builder.Services.AddSingleton<RenditionSelector>();
builder.Services.AddSingleton<GeneratedMediaValidator>();
builder.Services.AddSingleton<RenditionKeyFactory>();
builder.Services.AddSingleton<IVideoEncoder, FfmpegVideoEncoder>();
builder.Services.AddSingleton<IHlsPackager, FfmpegHlsPackager>();
builder.Services.AddSingleton<HlsPackageValidator>();
builder.Services.AddSingleton<HlsManifestBuilder>();
builder.Services.AddSingleton<HlsObjectKeyFactory>();
builder.Services.AddSingleton<ITranscodingPipeline, TranscodingPipeline>();
builder.Services.AddSingleton<IKafkaOutboxPublisher, KafkaOutboxPublisher>();
builder.Services.AddScoped<IMessageIngestor, MessageIngestor>();
builder.Services.AddSingleton<IJobStore, JobStore>();
builder.Services.AddSingleton<OutcomeMessageFactory>();
builder.Services.AddSingleton<TranscodingTelemetry>();

builder.Services.AddHostedService<InfrastructureInitializer>();
builder.Services.AddHostedService<KafkaIntakeService>();
builder.Services.AddHostedService<TranscodingJobService>();
builder.Services.AddHostedService<OutboxPublisherService>();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<StartupHealthCheck>("startup", tags: ["ready"])
    .AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"])
    .AddCheck<ObjectStorageHealthCheck>("minio", tags: ["ready"])
    .AddCheck<KafkaHealthCheck>("kafka", tags: ["ready"])
    .AddCheck<MediaToolsHealthCheck>("ffmpeg", tags: ["ready"])
    .AddCheck<ScratchStorageHealthCheck>("scratch", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapHealthChecks("/health");

app.Run();

static bool TopicNamesAreValid(KafkaOptions options)
{
    var topics = new[]
    {
        options.InputTopic,
        options.CompletedTopic,
        options.FailedTopic,
        options.DeadLetterTopic
    };
    return topics.All(topic => !string.IsNullOrWhiteSpace(topic)) &&
        topics.Distinct(StringComparer.Ordinal).Count() == topics.Length;
}

/// <summary>Exposes the application entry point for integration-test hosting.</summary>
public partial class Program;
