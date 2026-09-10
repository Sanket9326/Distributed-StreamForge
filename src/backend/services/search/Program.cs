using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StreamForge.Search.Api.Health;
using StreamForge.Search.Api.Middleware;
using StreamForge.Search.Api.Options;
using StreamForge.Search.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SearchExceptionHandler>();
builder.Services.AddControllers();
builder.Services.AddOptions<ElasticsearchOptions>()
    .BindConfiguration(ElasticsearchOptions.SectionName)
    .Validate(options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _), "Endpoint must be an absolute URI.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.IndexName), "IndexName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ReadAlias), "ReadAlias is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.WriteAlias), "WriteAlias is required.")
    .Validate(options => options.IndexName != options.ReadAlias && options.IndexName != options.WriteAlias, "Aliases must differ from the index name.")
    .Validate(options => options.RequestTimeoutSeconds > 0, "RequestTimeoutSeconds must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<KafkaOptions>()
    .BindConfiguration(KafkaOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.BootstrapServers), "BootstrapServers is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConsumerGroupId), "ConsumerGroupId is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.InputTopic), "InputTopic is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DeadLetterTopic), "DeadLetterTopic is required.")
    .Validate(options => options.InputTopic != options.DeadLetterTopic, "Kafka topics must be distinct.")
    .Validate(options => options.PartitionCount > 0, "PartitionCount must be positive.")
    .Validate(options => options.ReplicationFactor > 0, "ReplicationFactor must be positive.")
    .Validate(options => options.InitializationTimeoutSeconds > 0, "InitializationTimeoutSeconds must be positive.")
    .Validate(options => options.MaximumRetryDelaySeconds > 0, "MaximumRetryDelaySeconds must be positive.")
    .ValidateOnStart();

builder.Services.AddSingleton(services =>
{
    var options = services.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
    var settings = new ElasticsearchClientSettings(new Uri(options.Endpoint))
        .RequestTimeout(TimeSpan.FromSeconds(options.RequestTimeoutSeconds))
        .MaximumRetries(0);
    return new ElasticsearchClient(settings);
});
builder.Services.AddSingleton<IVideoSearchIndex, ElasticsearchVideoIndex>();
builder.Services.AddSingleton<KafkaTopicManager>();
builder.Services.AddSingleton<ISearchDeadLetterPublisher, KafkaDeadLetterPublisher>();
builder.Services.AddSingleton<SearchTelemetry>();
builder.Services.AddSingleton<IndexingMessageHandler>();
builder.Services.AddSingleton<StartupGate>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<InfrastructureInitializer>();
builder.Services.AddHostedService<SearchIndexConsumerService>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<SearchReadinessHealthCheck>("search_dependencies", tags: ["ready"]);

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
