using System.Net;
using System.Net.Http.Json;

namespace StreamForge.Gateway.IntegrationTests;

public sealed class GatewayRoutingTests(GatewayApiFactory factory) : IClassFixture<GatewayApiFactory>
{
    [Fact]
    public async Task UploadRoute_ForwardsBodyAndGeneratedCorrelationId()
    {
        using var client = factory.CreateClient();
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
        using var client = factory.CreateClient();
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
        using var client = factory.CreateClient();
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
    public async Task UnknownRoute_ReturnsNotFound()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/catalog");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record DownstreamResponse(long ReceivedBytes, string CorrelationId);
}
