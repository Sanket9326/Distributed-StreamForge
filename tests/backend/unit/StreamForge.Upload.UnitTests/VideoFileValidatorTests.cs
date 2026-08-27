using Microsoft.AspNetCore.Http;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.UnitTests;

public sealed class VideoFileValidatorTests
{
    private readonly VideoFileValidator validator = new();

    [Theory]
    [InlineData("source.mp4", "video/mp4", ".mp4")]
    [InlineData("source.MOV", "video/quicktime", ".mov")]
    [InlineData("source.webm", "video/webm", ".webm")]
    [InlineData("source.mkv", "video/x-matroska", ".mkv")]
    public void Validate_ReturnsNormalizedExtension(
        string fileName,
        string contentType,
        string expectedExtension)
    {
        var extension = validator.Validate(fileName, contentType);

        Assert.Equal(expectedExtension, extension);
    }

    [Theory]
    [InlineData("source.txt", "video/mp4")]
    [InlineData("source.exe", "video/mp4")]
    [InlineData("source.mp4", "application/octet-stream")]
    [InlineData("source.mp4", "text/plain")]
    public void Validate_RejectsUnsupportedMetadata(string fileName, string contentType)
    {
        var exception = Assert.Throws<UploadRequestException>(() =>
            validator.Validate(fileName, contentType));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, exception.StatusCode);
    }
}
