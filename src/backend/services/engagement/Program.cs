using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StreamForge.Engagement.Api.Data;
using StreamForge.Engagement.Api.Health;
using StreamForge.Engagement.Api.Middleware;
using StreamForge.Engagement.Api.Options;
using StreamForge.Engagement.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<EngagementExceptionHandler>();

builder.Services.AddOptions<KafkaOptions>().BindConfiguration(KafkaOptions.SectionName)
    .Validate(x => !string.IsNullOrWhiteSpace(x.BootstrapServers), "Kafka bootstrap servers are required.")
    .Validate(x => new[] { x.ReactionTopic, x.ViewTopic, x.CompletedTopic }.Distinct().Count() == 3, "Kafka topics must be distinct.")
    .Validate(x => x.PartitionCount > 0 && x.ReplicationFactor > 0, "Kafka topic settings are invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<EngagementOptions>().BindConfiguration(EngagementOptions.SectionName)
    .Validate(x => x.ViewAggregationWindowSeconds > 0 && x.MaximumViewBatchSize > 0 && x.ViewSessionTtlHours > 0,
        "Engagement batching settings must be positive.").ValidateOnStart();
builder.Services.AddOptions<FeedOptions>().BindConfiguration(FeedOptions.SectionName)
    .Validate(x => Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out _), "Feed base URL is required.").ValidateOnStart();

var connection = builder.Configuration.GetConnectionString("EngagementDatabase");
if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Engagement database configuration is required.");
builder.Services.AddDbContextFactory<EngagementDbContext>(options => options.UseNpgsql(connection, npgsql =>
{
    npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
    npgsql.MigrationsHistoryTable("__ef_migrations_history", EngagementDbContext.Schema);
}));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(
    builder.Configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Redis configuration is required.")));
builder.Services.AddHttpClient("feed", (services, client) =>
    client.BaseAddress = new Uri(services.GetRequiredService<IOptions<FeedOptions>>().Value.BaseUrl.TrimEnd('/') + "/"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StartupGate>();
builder.Services.AddSingleton<CommentCursorCodec>();
builder.Services.AddSingleton<KafkaTopicManager>();
builder.Services.AddSingleton<EngagementKafkaPublisher>();
builder.Services.AddSingleton<RedisEngagementProjection>();
builder.Services.AddScoped<KnownVideoService>();
builder.Services.AddScoped<CommentService>();
builder.Services.AddHostedService<InfrastructureInitializer>();
builder.Services.AddHostedService<VideoCatalogConsumer>();
builder.Services.AddHostedService<ReactionConsumer>();
builder.Services.AddHostedService<ViewAggregationConsumer>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<EngagementReadiness>("engagement-dependencies", tags: ["ready"]);

var app = builder.Build();
app.Use(async (context, next) =>
{
    var correlation = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (correlation is { Length: > 0 and <= 128 } && correlation.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        context.TraceIdentifier = correlation;
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    await next(context);
});
app.UseExceptionHandler();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = x => x.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = x => x.Tags.Contains("ready") });
app.MapHealthChecks("/health");
app.Run();

public partial class Program;
