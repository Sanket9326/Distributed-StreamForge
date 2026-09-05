using System.Net;
using System.Net.Http.Json;

namespace StreamForge.Gateway.IntegrationTests;

public sealed class GatewayRoutingTests(GatewayApiFactory factory) : IClassFixture<GatewayApiFactory>
{
    [Fact]
    public async Task UploadRoute_ForwardsBodyAndGeneratedCorrelationId()
    {
        using var client = await factory.AuthenticatedClientAsync();
        using var content = new ByteArrayContent([1, 2, 3, 4, 5]);

        using var response = await client.PostAsync("/api/uploads", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DownstreamResponse>();
        Assert.NotNull(result);
        Assert.Equal(5, result.ReceivedBytes);
        Assert.False(string.IsNullOrWhiteSpace(result.CorrelationId));
        Assert.Equal(
            result.CorrelationId,
            response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task UploadRoute_PreservesCallerCorrelationId()
    {
        using var client = await factory.AuthenticatedClientAsync();
        const string correlationId = "caller-correlation-id";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        using var response = await client.PostAsync("/api/uploads", new ByteArrayContent([1]));
        var result = await response.Content.ReadFromJsonAsync<DownstreamResponse>();

        Assert.Equal(correlationId, result?.CorrelationId);
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task UploadRoute_PassesThroughDownstreamProblemDetails()
    {
        using var client = await factory.AuthenticatedClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/uploads")
        {
            Content = new ByteArrayContent([1])
        };
        request.Headers.Add("X-Test-Response-Status", "415");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Contains("Downstream rejection", body);
        Assert.Contains("Rejected by upload service", body);
    }

    [Fact]
    public async Task FeedRoute_ForwardsQueryAndCorrelationId()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/feed/videos?limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<FeedDownstreamResponse>();
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.False(string.IsNullOrWhiteSpace(result.CorrelationId));
        Assert.Equal(
            result.CorrelationId,
            response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task FeedCompletionRoute_PreservesEventStreamResponse()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/feed/videos/e2c1bb10-4340-452f-9fc6-a68cf4b12457/completion-events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("event: completed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownRoute_ReturnsNotFound()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/catalog");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PlaybackRoute_PreservesManifestContentType()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/playback/videos/e2c1bb10-4340-452f-9fc6-a68cf4b12457/master.m3u8");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.apple.mpegurl", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record DownstreamResponse(long ReceivedBytes, string CorrelationId);

    private sealed record FeedDownstreamResponse(object[] Items, string? NextCursor, string CorrelationId);
}
