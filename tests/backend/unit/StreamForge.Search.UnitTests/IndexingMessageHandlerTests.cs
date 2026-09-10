using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StreamForge.Search.Api.Models;
using StreamForge.Search.Api.Services;

namespace StreamForge.Search.UnitTests;

public sealed class IndexingMessageHandlerTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(8, 30)]
    public void RetryDelay_IsExponentialAndCapped(int attempt, int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            SearchIndexConsumerService.CalculateRetryDelay(attempt, 30));
    }

    [Fact]
    public async Task HandleAsync_LeavesTransientFailureForRetryWithoutDeadLettering()
    {
        var index = new FakeIndex(IndexWriteResult.TransientFailure);
        var deadLetters = new FakeDeadLetters();
        using var telemetry = new SearchTelemetry();
        var handler = new IndexingMessageHandler(
            index,
            deadLetters,
            telemetry,
            TimeProvider.System,
            NullLogger<IndexingMessageHandler>.Instance);

        var result = await handler.HandleAsync(Envelope(SearchEventParserTests.Requested(1)), CancellationToken.None);

        Assert.Equal(IndexingHandleResult.Retry, result);
        Assert.Empty(deadLetters.Events);
    }

    [Theory]
    [InlineData(IndexWriteResult.Indexed)]
    [InlineData(IndexWriteResult.Superseded)]
    public async Task HandleAsync_TreatsAppliedAndOlderRevisionsAsComplete(IndexWriteResult writeResult)
    {
        var index = new FakeIndex(writeResult);
        var deadLetters = new FakeDeadLetters();
        using var telemetry = new SearchTelemetry();
        var handler = new IndexingMessageHandler(index, deadLetters, telemetry, TimeProvider.System,
            NullLogger<IndexingMessageHandler>.Instance);

        var result = await handler.HandleAsync(Envelope(SearchEventParserTests.Requested(1)), CancellationToken.None);

        Assert.Equal(IndexingHandleResult.Completed, result);
        Assert.Empty(deadLetters.Events);
    }

    [Fact]
    public async Task HandleAsync_DeadLettersMalformedPayloadBeforeCompleting()
    {
        var deadLetters = new FakeDeadLetters();
        using var telemetry = new SearchTelemetry();
        var handler = new IndexingMessageHandler(new FakeIndex(IndexWriteResult.Indexed), deadLetters, telemetry,
            TimeProvider.System, NullLogger<IndexingMessageHandler>.Instance);

        var result = await handler.HandleAsync(
            new SearchConsumedEnvelope("video-search-index", 0, 4, "video", "bad-json"),
            CancellationToken.None);

        Assert.Equal(IndexingHandleResult.Completed, result);
        Assert.Equal("malformed_json", Assert.Single(deadLetters.Events).Reason);
    }

    [Fact]
    public async Task HandleAsync_DeadLettersPermanentMappingFailureBeforeCompleting()
    {
        var deadLetters = new FakeDeadLetters();
        using var telemetry = new SearchTelemetry();
        var handler = new IndexingMessageHandler(
            new FakeIndex(IndexWriteResult.PermanentFailure),
            deadLetters,
            telemetry,
            TimeProvider.System,
            NullLogger<IndexingMessageHandler>.Instance);

        var result = await handler.HandleAsync(Envelope(SearchEventParserTests.Requested(1)), CancellationToken.None);

        Assert.Equal(IndexingHandleResult.Completed, result);
        Assert.Equal("permanent_mapping_error", Assert.Single(deadLetters.Events).Reason);
    }

    [Fact]
    public async Task HandleAsync_RetriesMalformedMessageWhenDeadLetterIsNotAcknowledged()
    {
        var deadLetters = new FakeDeadLetters { Fail = true };
        using var telemetry = new SearchTelemetry();
        var handler = new IndexingMessageHandler(new FakeIndex(IndexWriteResult.Indexed), deadLetters, telemetry,
            TimeProvider.System, NullLogger<IndexingMessageHandler>.Instance);

        var result = await handler.HandleAsync(
            new SearchConsumedEnvelope("video-search-index", 0, 4, "video", "bad-json"),
            CancellationToken.None);

        Assert.Equal(IndexingHandleResult.Retry, result);
    }

    private static SearchConsumedEnvelope Envelope(VideoSearchIndexRequestedV1 requested) => new(
        "video-search-index",
        0,
        3,
        requested.VideoId.ToString("D"),
        JsonSerializer.Serialize(requested, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private sealed class FakeIndex(IndexWriteResult result) : IVideoSearchIndex
    {
        public Task EnsureCreatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task VerifyAvailableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IndexWriteResult> IndexAsync(VideoSearchIndexRequestedV1 requested, CancellationToken cancellationToken) =>
            Task.FromResult(result);
        public Task<IReadOnlyList<VideoSuggestion>> SuggestAsync(string query, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VideoSuggestion>>([]);
    }

    private sealed class FakeDeadLetters : ISearchDeadLetterPublisher
    {
        public List<SearchIndexDeadLetterV1> Events { get; } = [];
        public bool Fail { get; init; }

        public Task PublishAsync(SearchIndexDeadLetterV1 deadLetter, string partitionKey, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("DLQ unavailable");
            Events.Add(deadLetter);
            return Task.CompletedTask;
        }
    }
}
