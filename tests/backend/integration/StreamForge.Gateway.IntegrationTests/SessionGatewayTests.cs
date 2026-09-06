using System.Net;
using System.Net.Http.Json;
using StreamForge.Gateway.Api.Authentication;

namespace StreamForge.Gateway.IntegrationTests;

public sealed class SessionGatewayTests(GatewayApiFactory factory) : IClassFixture<GatewayApiFactory>
{
    [Theory]
    [InlineData(null)]
    [InlineData("expired")]
    [InlineData("invalid")]
    public async Task Upload_WithoutLiveSession_RejectsBeforeProxying(string? cookie)
    {
        using var client = factory.CreateClient();
        if (cookie is not null) client.DefaultRequestHeaders.Add("Cookie", $"{RedisSessionReader.CookieName}={cookie}");
        using var response = await client.PostAsync("/api/uploads", new StringContent("media"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), x => x.Contains("expires="));
    }

    [Fact]
    public async Task Upload_RedisUnavailable_PreservesCookieAndReturns503()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{RedisSessionReader.CookieName}=redis-down");
        using var response = await client.PostAsync("/api/uploads", new StringContent("media"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var feed = await client.GetAsync("/api/feed/videos");
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
    }

    [Fact]
    public async Task Upload_ValidSession_StripsCookiesAndReplacesForgedOwner()
    {
        using var client = await factory.AuthenticatedClientAsync();
        client.DefaultRequestHeaders.Add("X-StreamForge-User-Id", Guid.NewGuid().ToString());
        using var response = await client.PostAsync("/api/uploads", new StringContent("media"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(GatewayApiFactory.UserId.ToString(), body.GetProperty("ownerId").GetString());
        Assert.Equal("", body.GetProperty("cookie").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("forged-token")]
    public async Task Upload_InvalidAntiforgery_RejectsBeforeProxying(string? token)
    {
        using var client = await factory.AuthenticatedClientAsync();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        if (token is not null) client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
        using var response = await client.PostAsync("/api/uploads", new StringContent("media"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/logout")]
    public async Task AnonymousAuthMutation_RequiresAntiforgery(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EngagementReaction_RequiresSessionAndForwardsOnlyVerifiedIdentity()
    {
        var path = $"/api/engagement/videos/{Guid.NewGuid():D}/reaction";
        using var anonymous = await factory.AnonymousClientAsync();
        using var rejected = await anonymous.PutAsJsonAsync(path, new { reaction = "like" });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        using var authenticated = await factory.AuthenticatedClientAsync();
        authenticated.DefaultRequestHeaders.Add("X-StreamForge-User-Id", Guid.NewGuid().ToString());
        using var accepted = await authenticated.PutAsJsonAsync(path, new { reaction = "like" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(GatewayApiFactory.UserId.ToString(), body.GetProperty("userId").GetString());
        Assert.Equal(string.Empty, body.GetProperty("cookie").GetString());
    }

    [Fact]
    public async Task EngagementView_IsPublicButStillRequiresAntiforgery()
    {
        var path = $"/api/engagement/videos/{Guid.NewGuid():D}/views";
        using var missingToken = factory.CreateClient();
        using var rejected = await missingToken.PostAsJsonAsync(path, new { viewSessionId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        using var anonymous = await factory.AnonymousClientAsync();
        using var accepted = await anonymous.PostAsJsonAsync(path, new { viewSessionId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task EngagementCommentMutations_RequireSession()
    {
        using var anonymous = await factory.AnonymousClientAsync();
        using var response = await anonymous.PostAsJsonAsync(
            $"/api/engagement/videos/{Guid.NewGuid():D}/comments", new { body = "hello" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
