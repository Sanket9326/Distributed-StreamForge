using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StreamForge.Upload.Api.Models;

namespace StreamForge.Upload.IntegrationTests;

public sealed class UploadsEndpointTests(UploadApiFactory factory) : IClassFixture<UploadApiFactory>
{
    [Fact]
    public async Task Upload_StoresBytesAndReturnsReceiptWithCorrelationId()
    {
        using var client = factory.CreateClient();
        const string correlationId = "integration-test-correlation";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        using var response = await client.PostAsync(
            "/api/uploads",
            CreateUpload("source.mp4", "video/mp4", [1, 2, 3, 4]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-ID").Single());

        var receipt = await response.Content.ReadFromJsonAsync<UploadResponse>();
        Assert.NotNull(receipt);
        Assert.Equal("source.mp4", receipt.FileName);
        Assert.Equal(4, receipt.SizeBytes);
        Assert.Equal(correlationId, receipt.CorrelationId);

        var storedPath = Path.Combine(factory.StorageRoot, $"{receipt.Id:N}.mp4");
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(storedPath));
        Assert.Empty(Directory.GetFiles(factory.StorageRoot, ".uploading-*"));
    }

    [Fact]
    public async Task Upload_UsesUniqueStorageNamesForDuplicateClientNames()
    {
        using var client = factory.CreateClient();

        using var firstResponse = await client.PostAsync(
            "/api/uploads",
            CreateUpload("duplicate.webm", "video/webm", [1]));
        using var secondResponse = await client.PostAsync(
            "/api/uploads",
            CreateUpload("duplicate.webm", "video/webm", [2]));

        var first = await firstResponse.Content.ReadFromJsonAsync<UploadResponse>();
        var second = await secondResponse.Content.ReadFromJsonAsync<UploadResponse>();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Id, second.Id);
        Assert.True(File.Exists(Path.Combine(factory.StorageRoot, $"{first.Id:N}.webm")));
        Assert.True(File.Exists(Path.Combine(factory.StorageRoot, $"{second.Id:N}.webm")));
    }

    [Fact]
    public async Task Upload_RejectsOversizedVideoAndRemovesPartialFile()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/uploads",
            CreateUpload("large.mkv", "video/x-matroska", new byte[9]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Video is too large", problem?.Title);
        Assert.Empty(Directory.GetFiles(factory.StorageRoot, ".uploading-*"));
    }

    [Theory]
    [InlineData("source.txt", "video/mp4", HttpStatusCode.UnsupportedMediaType, false)]
    [InlineData("source.mp4", "text/plain", HttpStatusCode.UnsupportedMediaType, false)]
    [InlineData("source.mp4", "video/mp4", HttpStatusCode.BadRequest, true)]
    public async Task Upload_RejectsInvalidFile(
        string fileName,
        string contentType,
        HttpStatusCode expectedStatus,
        bool empty)
    {
        using var client = factory.CreateClient();
        var bytes = empty ? Array.Empty<byte>() : new byte[] { 1 };

        using var response = await client.PostAsync(
            "/api/uploads",
            CreateUpload(fileName, contentType, bytes));

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Upload_RequiresFileField()
    {
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("value"), "metadata");

        using var response = await client.PostAsync("/api/uploads", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsMultipleFilesAndRemovesPartialFile()
    {
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(CreateFile([1], "video/mp4"), "file", "first.mp4");
        content.Add(CreateFile([2], "video/mp4"), "file", "second.mp4");

        using var response = await client.PostAsync("/api/uploads", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.GetFiles(factory.StorageRoot, ".uploading-*"));
    }

    [Fact]
    public async Task Upload_ReturnsProblemDetailsWhenStorageIsUnavailable()
    {
        var blockingFile = Path.GetTempFileName();
        try
        {
            await using var failureFactory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureLogging(logging => logging.ClearProviders());
                    builder.ConfigureAppConfiguration((_, configuration) =>
                    {
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["UploadStorage:RootPath"] = blockingFile,
                            ["UploadStorage:MaxFileSizeBytes"] = "8"
                        });
                    });
                });
            using var client = failureFactory.CreateClient();

            using var response = await client.PostAsync(
                "/api/uploads",
                CreateUpload("source.mp4", "video/mp4", [1]));

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Equal("Upload storage failure", problem?.Title);
        }
        finally
        {
            File.Delete(blockingFile);
        }
    }

    [Fact]
    public async Task Health_ReportsWritableStorage()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static MultipartFormDataContent CreateUpload(
        string fileName,
        string contentType,
        byte[] bytes)
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(CreateFile(bytes, contentType), "file", fileName);
        return multipart;
    }

    private static ByteArrayContent CreateFile(byte[] bytes, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return file;
    }
}
