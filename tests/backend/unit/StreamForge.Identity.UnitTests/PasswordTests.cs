using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using StreamForge.Identity.Api.Data;
using StreamForge.Identity.Api.Services;

namespace StreamForge.Identity.UnitTests;

public sealed class PasswordTests
{
    private static PasswordHasher<User> Hasher(int iterations = 600_000) => new(Options.Create(new PasswordHasherOptions
    { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = iterations }));

    [Fact]
    public void Hash_SaltsIdenticalPasswordsAndPreservesWhitespace()
    {
        var service = new PasswordService(Hasher());
        var user = new User();
        const string password = "  a password with spaces  ";
        user.PasswordHash = service.Hash(user, password);
        Assert.NotEqual(user.PasswordHash, service.Hash(user, password));
        Assert.Equal(PasswordVerificationResult.Success, service.Verify(user, password));
        Assert.Equal(PasswordVerificationResult.Failed, service.Verify(user, password.Trim()));
        Assert.Equal(PasswordVerificationResult.Failed, service.Verify(null, password));
    }

    [Fact]
    public void Verify_OlderWorkFactor_RequestsRehash()
    {
        var user = new User();
        user.PasswordHash = Hasher(100_000).HashPassword(user, "a sufficiently long password");
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded,
            new PasswordService(Hasher()).Verify(user, "a sufficiently long password"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa+")]
    public void SessionKey_InvalidCookie_DoesNotCreateRedisKey(string? cookie) => Assert.Null(RedisSessionStore.Key(cookie));

    [Fact]
    public void Normalize_UsesSameValueForCaseAndSurroundingWhitespace() =>
        Assert.Equal(AccountService.Normalize("  Person@Example.Test "), AccountService.Normalize("person@example.test"));
}
