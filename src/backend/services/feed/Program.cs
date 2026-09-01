using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StreamForge.Feed.Api.Data;
using StreamForge.Feed.Api.Health;
using StreamForge.Feed.Api.Middleware;
using StreamForge.Feed.Api.Options;
using StreamForge.Feed.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<FeedExceptionHandler>();
builder.Services.AddControllers();

builder.Services.AddOptions<KafkaOptions>()
    .BindConfiguration(KafkaOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.BootstrapServers), "BootstrapServers is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConsumerGroupId), "ConsumerGroupId is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.UploadedTopic), "UploadedTopic is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.CompletedTopic), "CompletedTopic is required.")
    .Validate(options => options.UploadedTopic != options.CompletedTopic, "Feed topics must be distinct.")
    .Validate(options => options.InitializationTimeoutSeconds > 0, "InitializationTimeoutSeconds must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<ObjectStorageOptions>()
    .BindConfiguration(ObjectStorageOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Endpoint), "Endpoint is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.PublicEndpoint), "PublicEndpoint is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AccessKey), "AccessKey is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.SecretKey), "SecretKey is required.")
    .Validate(options => options.RenditionsBucket.Length is >= 3 and <= 63, "RenditionsBucket is invalid.")
    .Validate(options => options.SignedUrlExpirySeconds is >= 60 and <= 604_800, "SignedUrlExpirySeconds is invalid.")
    .ValidateOnStart();

var database = builder.Configuration.GetConnectionString("FeedDatabase");
if (string.IsNullOrWhiteSpace(database))
{
    throw new InvalidOperationException("ConnectionStrings:FeedDatabase is required.");
}

builder.Services.AddDbContextFactory<FeedDbContext>(options =>
    options.UseNpgsql(database, npgsql =>
    {
        npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
        npgsql.MigrationsHistoryTable("__ef_migrations_history", FeedDbContext.Schema);
    }));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StartupGate>();
builder.Services.AddSingleton<CompletionNotifier>();
builder.Services.AddSingleton<FeedCursorCodec>();
builder.Services.AddSingleton<KafkaTopicManager>();
builder.Services.AddSingleton<IRenditionStorage, MinioRenditionStorage>();
builder.Services.AddSingleton<IPlaybackUrlSigner, MinioPlaybackUrlSigner>();
builder.Services.AddScoped<IFeedEventProjector, FeedEventProjector>();
builder.Services.AddScoped<FeedQueryService>();
builder.Services.AddHostedService<InfrastructureInitializer>();
builder.Services.AddHostedService<KafkaIntakeService>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"])
    .AddCheck<KafkaHealthCheck>("kafka", tags: ["ready"])
    .AddCheck<ObjectStorageHealthCheck>("minio", tags: ["ready"]);

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.MapControllers();
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

public partial class Program;
