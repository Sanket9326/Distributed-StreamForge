using System.Net;
using System.Net.Sockets;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StreamForge.Transcoding.Worker.Services;
using Testcontainers.PostgreSql;

namespace StreamForge.Transcoding.IntegrationTests;

public sealed class TranscodingWorkerFactory : IAsyncLifetime
{
    public const string InputTopic = "video-processing";
    public const string CompletedTopic = "video-transcoding-completed";
    public const string FailedTopic = "video-transcoding-failed";
    public const string DeadLetterTopic = "video-processing-dead-letter";
    public const string MinioAccessKey = "streamforge-test";
    public const string MinioSecretKey = "streamforge-test-secret";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.6-alpine")
        .WithDatabase("streamforge_upload")
        .WithUsername("streamforge")
        .WithPassword("streamforge-test-password")
        .Build();
    private readonly int kafkaHostPort;
    private readonly IContainer kafka;
    private readonly IFutureDockerImage minioImage;
    private readonly IContainer minio;
    private WebApplicationFactory<Program>? application;

    public TranscodingWorkerFactory()
    {
        kafkaHostPort = FindAvailablePort();
        kafka = new ContainerBuilder("apache/kafka:4.3.1")
            .WithPortBinding(kafkaHostPort, 9094)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_LISTENERS", "INTERNAL://:9092,CONTROLLER://:9093,EXTERNAL://:9094")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", $"INTERNAL://localhost:9092,EXTERNAL://127.0.0.1:{kafkaHostPort}")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "INTERNAL")
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "CONTROLLER:PLAINTEXT,INTERNAL:PLAINTEXT,EXTERNAL:PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:9093")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted(
                "/opt/kafka/bin/kafka-topics.sh",
                "--bootstrap-server",
                "127.0.0.1:9092",
                "--list"))
            .Build();

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

    public string KafkaBootstrapServers => $"127.0.0.1:{kafkaHostPort}";

    public string PostgresConnectionString => $"{postgres.GetConnectionString()};Timeout=2;Command Timeout=2";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), kafka.StartAsync(), minioImage.CreateAsync());
        await minio.StartAsync();
        await CreateInputTopicAsync();

        application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITranscodingPipeline>();
                services.AddSingleton<ITranscodingPipeline, FakeTranscodingPipeline>();
            });
            var settings = new Dictionary<string, string>
            {
                ["ConnectionStrings:TranscodingDatabase"] = PostgresConnectionString,
                ["Kafka:BootstrapServers"] = KafkaBootstrapServers,
                ["Kafka:ConsumerGroupId"] = $"streamforge-transcoding-tests-{Guid.NewGuid():N}",
                ["Kafka:InputTopic"] = InputTopic,
                ["Kafka:CompletedTopic"] = CompletedTopic,
                ["Kafka:FailedTopic"] = FailedTopic,
                ["Kafka:DeadLetterTopic"] = DeadLetterTopic,
                ["Kafka:InitializationTimeoutSeconds"] = "30",
                ["ObjectStorage:Endpoint"] = $"{minio.Hostname}:{minio.GetMappedPublicPort(9000)}",
                ["ObjectStorage:AccessKey"] = MinioAccessKey,
                ["ObjectStorage:SecretKey"] = MinioSecretKey,
                ["ObjectStorage:RenditionsBucket"] = "streamforge-renditions",
                ["ObjectStorage:UseSsl"] = "false",
                ["Transcoding:MaxConcurrentJobs"] = "2",
                ["Transcoding:PollIntervalMilliseconds"] = "25",
                ["Transcoding:LeaseDurationSeconds"] = "10",
                ["Transcoding:LeaseHeartbeatSeconds"] = "2",
                ["Transcoding:RetryBaseDelaySeconds"] = "1",
                ["Transcoding:RetryMaximumDelaySeconds"] = "2",
                ["Transcoding:ScratchPath"] = Path.Combine(Path.GetTempPath(), $"streamforge-transcoding-tests-{Guid.NewGuid():N}"),
                ["Transcoding:MinimumFreeScratchBytes"] = "0",
                ["Outbox:PollIntervalMilliseconds"] = "25",
                ["Outbox:BatchSize"] = "10",
                ["Outbox:MaximumRetryDelaySeconds"] = "2"
            };
            foreach (var setting in settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });

        using var client = application.CreateClient();
        using var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync();
        }

        await minio.DisposeAsync();
        await kafka.DisposeAsync();
        await postgres.DisposeAsync();
        await minioImage.DisposeAsync();
    }

    private async Task CreateInputTopicAsync()
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = KafkaBootstrapServers
        }).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification
            {
                Name = InputTopic,
                NumPartitions = 1,
                ReplicationFactor = 1
            }
        ]);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "StreamForge.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find the StreamForge repository root.");
    }

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
