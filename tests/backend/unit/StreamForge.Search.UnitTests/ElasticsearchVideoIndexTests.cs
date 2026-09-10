using System.Text.Json;
using StreamForge.Search.Api.Services;

namespace StreamForge.Search.UnitTests;

public sealed class ElasticsearchVideoIndexTests
{
    [Fact]
    public void ToDocument_NormalizesHashtagsAndKeepsRevision()
    {
        var requested = SearchEventParserTests.Requested(7) with
        {
            Hashtags = [" #DotNet ", "dotnet", "#SEARCH"]
        };

        var document = ElasticsearchVideoIndex.ToDocument(requested);

        Assert.Equal(7, document.Revision);
        Assert.Equal(["dotnet", "search"], document.Hashtags);
        Assert.Equal("dotnet search", document.HashtagsText);
    }

    [Fact]
    public void BuildSearchPayload_UsesBoostedBoolPrefixFields()
    {
        using var payload = JsonDocument.Parse(ElasticsearchVideoIndex.BuildSearchPayload("elas", 8));
        var multiMatch = payload.RootElement.GetProperty("query").GetProperty("multi_match");
        var fields = multiMatch.GetProperty("fields").EnumerateArray().Select(item => item.GetString()).ToArray();

        Assert.Equal("bool_prefix", multiMatch.GetProperty("type").GetString());
        Assert.Contains("title^4", fields);
        Assert.Contains("hashtagsText^2", fields);
        Assert.Contains("description", fields);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(409, false)]
    public void IsTransientStatus_ClassifiesRetryableResponses(int? statusCode, bool expected)
    {
        Assert.Equal(expected, ElasticsearchVideoIndex.IsTransientStatus(statusCode));
    }
}
