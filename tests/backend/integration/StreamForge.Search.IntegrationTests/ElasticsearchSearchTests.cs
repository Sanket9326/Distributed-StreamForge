using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StreamForge.Search.Api.Models;
using StreamForge.Search.Api.Options;
using StreamForge.Search.Api.Services;

namespace StreamForge.Search.IntegrationTests;

public sealed class ElasticsearchSearchTests : IAsyncLifetime
{
    private readonly IContainer elasticsearch = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.5.3")
        .WithPortBinding(9200, assignRandomHostPort: true)
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("xpack.security.enrollment.enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
            request.ForPort(9200).ForPath("/_cluster/health?wait_for_status=yellow")))
        .Build();
    private ElasticsearchClient client = null!;
    private ElasticsearchVideoIndex index = null!;

    public async Task InitializeAsync()
    {
        await elasticsearch.StartAsync();
        var endpoint = new Uri($"http://{elasticsearch.Hostname}:{elasticsearch.GetMappedPublicPort(9200)}");
        client = new ElasticsearchClient(new ElasticsearchClientSettings(endpoint).MaximumRetries(0));
        index = new ElasticsearchVideoIndex(
            client,
            Options.Create(new ElasticsearchOptions { Endpoint = endpoint.ToString() }),
            NullLogger<ElasticsearchVideoIndex>.Instance);
        await index.EnsureCreatedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await elasticsearch.DisposeAsync();

    [Fact]
    public async Task Suggestions_RankTitleThenHashtagThenDescription()
    {
        var title = Requested(Guid.NewGuid(), 1, "Elastic pipelines", [], "ordinary");
        var hashtag = Requested(Guid.NewGuid(), 1, "Ordinary title", ["elastic"], "ordinary");
        var description = Requested(Guid.NewGuid(), 1, "Another title", [], "elastic internals");
        Assert.Equal(IndexWriteResult.Indexed, await index.IndexAsync(title, CancellationToken.None));
        Assert.Equal(IndexWriteResult.Indexed, await index.IndexAsync(hashtag, CancellationToken.None));
        Assert.Equal(IndexWriteResult.Indexed, await index.IndexAsync(description, CancellationToken.None));
        await RefreshAsync();

        var suggestions = await index.SuggestAsync("elas", 8, CancellationToken.None);

        Assert.Equal([title.VideoId, hashtag.VideoId, description.VideoId],
            suggestions.Select(item => Guid.Parse(item.VideoId)).ToArray());
    }

    [Fact]
    public async Task Indexing_ReplayAndHigherRevisionAreAcceptedWhileOlderRevisionIsSuperseded()
    {
        var videoId = Guid.NewGuid();
        var first = Requested(videoId, 1, "Original title", ["original"], null);
        var updated = Requested(videoId, 2, "Updated title", ["updated"], null);

        Assert.Equal(IndexWriteResult.Indexed, await index.IndexAsync(first, CancellationToken.None));
        Assert.Equal(IndexWriteResult.Indexed, await index.IndexAsync(first, CancellationToken.None));
        Assert.Equal(IndexWriteResult.Indexed, await index.IndexAsync(updated, CancellationToken.None));
        Assert.Equal(IndexWriteResult.Superseded, await index.IndexAsync(first, CancellationToken.None));
        await RefreshAsync();

        var suggestion = Assert.Single(await index.SuggestAsync("updated", 8, CancellationToken.None));
        Assert.Equal(videoId.ToString("D"), suggestion.VideoId);
        Assert.Equal("Updated title", suggestion.Title);
    }

    private async Task RefreshAsync()
    {
        var response = await client.Transport.PostAsync<ElasticsearchStringResponse>(
            "/streamforge-videos-write/_refresh",
            PostData.String("{}"),
            CancellationToken.None);
        Assert.True(response.IsValidResponse, response.DebugInformation);
    }

    private static VideoSearchIndexRequestedV1 Requested(
        Guid videoId,
        long revision,
        string title,
        IReadOnlyList<string> hashtags,
        string? description) => new(
            Guid.NewGuid(),
            VideoSearchIndexRequestedV1.Type,
            VideoSearchIndexRequestedV1.Version,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "integration-correlation",
            videoId,
            revision,
            Guid.NewGuid(),
            title,
            description,
            hashtags,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow.AddMinutes(-1));
}
