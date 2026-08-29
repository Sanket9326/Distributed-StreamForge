using Microsoft.AspNetCore.Http;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.UnitTests;

public sealed class UploadMetadataValidatorTests
{
    private readonly UploadMetadataValidator validator = new();

    [Fact]
    public void Validate_TrimsMetadataAndNormalizesDistinctHashtags()
    {
        var metadata = validator.Validate(
            "  A title  ",
            "  A description  ",
            [" #DotNet, VIDEO ", "dotnet", "event-driven"]);

        Assert.Equal("A title", metadata.Title);
        Assert.Equal("A description", metadata.Description);
        Assert.Equal(["dotnet", "video", "event-driven"], metadata.Hashtags);
    }

    [Fact]
    public void Validate_ConvertsBlankDescriptionToNull()
    {
        var metadata = validator.Validate("Title", "   ", []);

        Assert.Null(metadata.Description);
        Assert.Empty(metadata.Hashtags);
    }

    [Theory]
    [MemberData(nameof(InvalidMetadata))]
    public void Validate_RejectsInvalidMetadata(
        string? title,
        string? description,
        string[] hashtags)
    {
        var exception = Assert.Throws<UploadRequestException>(() =>
            validator.Validate(title, description, hashtags));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    public static TheoryData<string?, string?, string[]> InvalidMetadata =>
        new()
        {
            { null, null, [] },
            { " ", null, [] },
            { new string('t', 201), null, [] },
            { "Title", new string('d', 5_001), [] },
            { "Title", null, [new string('h', 51)] },
            { "Title", null, ["not valid"] },
            {
                "Title",
                null,
                ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven"]
            }
        };
}
