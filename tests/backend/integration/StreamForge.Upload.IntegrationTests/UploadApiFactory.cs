using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace StreamForge.Upload.IntegrationTests;

public sealed class UploadApiFactory : IAsyncLifetime
{
    public const string MinioAccessKey = "streamforge-test";
    public const string MinioSecretKey = "streamforge-test-secret";
    public const string BucketName = "streamforge-videos";
    public const string TopicName = "video-processing";

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

    public UploadApiFactory()
    {
        kafkaHostPort = FindAvailablePort();
        kafka = new ContainerBuilder("apache/kafka:4.3.1")
            .WithPortBinding(kafkaHostPort, 9094)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment(
                "KAFKA_LISTENERS",
                "INTERNAL://:9092,CONTROLLER://:9093,EXTERNAL://:9094")
            .WithEnvironment(
                "KAFKA_ADVERTISED_LISTENERS",
                $"INTERNAL://localhost:9092,EXTERNAL://127.0.0.1:{kafkaHostPort}")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "INTERNAL")
            .WithEnvironment(
                "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
                "CONTROLLER:PLAINTEXT,INTERNAL:PLAINTEXT,EXTERNAL:PLAINTEXT")
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

    public string PostgresConnectionString =>
        $"{postgres.GetConnectionString()};Timeout=2;Command Timeout=2";

    public string KafkaBootstrapServers => $"127.0.0.1:{kafkaHostPort}";

    public string MinioEndpoint => $"{minio.Hostname}:{minio.GetMappedPublicPort(9000)}";

    public IServiceProvider Services =>
        application?.Services ?? throw new InvalidOperationException("The test application is not running.");

    public static readonly Guid OwnerId = Guid.Parse("e2c1bb10-4340-452f-9fc6-a68cf4b12457");

    public HttpClient CreateClient()
    {
        var client = application?.CreateClient() ?? throw new InvalidOperationException("The test application is not running.");
        client.DefaultRequestHeaders.Add("X-StreamForge-User-Id", OwnerId.ToString());
        return client;
    }

    public Task PauseKafkaAsync() => kafka.PauseAsync();

    public Task UnpauseKafkaAsync() => kafka.UnpauseAsync();

    public Task PausePostgresAsync() => postgres.PauseAsync();

    public Task UnpausePostgresAsync() => postgres.UnpauseAsync();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), kafka.StartAsync(), minioImage.CreateAsync());
        await minio.StartAsync();

        application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging => logging.ClearProviders());
                var settings = new Dictionary<string, string>
                {
                    ["ConnectionStrings:UploadDatabase"] = PostgresConnectionString,
                    ["Upload:MaxFileSizeBytes"] = "8",
                    ["ObjectStorage:Endpoint"] = MinioEndpoint,
                    ["ObjectStorage:AccessKey"] = MinioAccessKey,
                    ["ObjectStorage:SecretKey"] = MinioSecretKey,
                    ["ObjectStorage:Bucket"] = BucketName,
                    ["ObjectStorage:UseSsl"] = "false",
                    ["Kafka:BootstrapServers"] = KafkaBootstrapServers,
                    ["Kafka:TopicName"] = TopicName,
                    ["Kafka:PartitionCount"] = "1",
                    ["Kafka:ReplicationFactor"] = "1",
                    ["Kafka:InitializationTimeoutSeconds"] = "30",
                    ["Outbox:PollIntervalMilliseconds"] = "50",
                    ["Outbox:BatchSize"] = "10",
                    ["Outbox:MaximumRetryDelaySeconds"] = "2",
                    ["Outbox:DegradedAfterSeconds"] = "30"
                };
                foreach (var setting in settings)
                {
                    builder.UseSetting(setting.Key, setting.Value);
                }
            });

        using var client = application.CreateClient();
        using var health = await client.GetAsync("/health");
        health.EnsureSuccessStatusCode();
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

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
