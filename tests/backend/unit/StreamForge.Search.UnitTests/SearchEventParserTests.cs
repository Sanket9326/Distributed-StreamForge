using System.Text.Json;
using StreamForge.Search.Api.Models;
using StreamForge.Search.Api.Services;

namespace StreamForge.Search.UnitTests;

public sealed class SearchEventParserTests
{
    [Fact]
    public void Parse_AcceptsTheVersionedContract()
    {
        var requested = Requested(1);

        var result = SearchEventParser.Parse(JsonSerializer.Serialize(
            requested,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.NotNull(result.Event);
        Assert.Equal(requested.EventId, result.Event.EventId);
        Assert.Equal(requested.VideoId, result.Event.VideoId);
        Assert.Equal(requested.Revision, result.Event.Revision);
        Assert.Equal(requested.Hashtags, result.Event.Hashtags);
        Assert.Null(result.RejectionReason);
    }

    [Theory]
    [InlineData("not-json", "malformed_json")]
    [InlineData("{}", "invalid_contract")]
    public void Parse_RejectsMalformedAndInvalidContracts(string payload, string reason)
    {
        var result = SearchEventParser.Parse(payload);

        Assert.Null(result.Event);
        Assert.Equal(reason, result.RejectionReason);
    }

    internal static VideoSearchIndexRequestedV1 Requested(long revision) => new(
        Guid.NewGuid(),
        VideoSearchIndexRequestedV1.Type,
        VideoSearchIndexRequestedV1.Version,
        DateTimeOffset.UtcNow,
        Guid.NewGuid(),
        "correlation",
        Guid.NewGuid(),
        revision,
        Guid.NewGuid(),
        "Elastic Video",
        "A searchable description",
        ["#DotNet", "Search"],
        DateTimeOffset.UtcNow.AddMinutes(-2),
        DateTimeOffset.UtcNow.AddMinutes(-1));
}
