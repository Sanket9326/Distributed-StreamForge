using Microsoft.AspNetCore.Identity;
using StreamForge.Identity.Api.Data;

namespace StreamForge.Identity.Api.Services;

/// <summary>Hashes and verifies credentials, including equivalent work for nonexistent users.</summary>
public sealed class PasswordService(IPasswordHasher<User> hasher)
{
    private readonly User dummyUser = new();
    private readonly string dummyHash = hasher.HashPassword(new User(), "dummy-password-never-used-for-login");

    /// <summary>Creates a salted, versioned hash of an unmodified password.</summary>
    /// <param name="user">The account receiving the hash.</param>
    /// <param name="password">The exact supplied password, including whitespace.</param>
    /// <returns>The framework's versioned salted hash.</returns>
    public string Hash(User user, string password) => hasher.HashPassword(user, password);

    /// <summary>Verifies a password, doing real hash work even when no account exists.</summary>
    /// <param name="user">The matching account, or null for an unknown email.</param>
    /// <param name="password">The unmodified candidate password.</param>
    /// <returns>Failure, success, or success requiring a stronger hash.</returns>
    public PasswordVerificationResult Verify(User? user, string password)
    {
        var result = hasher.VerifyHashedPassword(user ?? dummyUser, user?.PasswordHash ?? dummyHash, password);
        return user is null ? PasswordVerificationResult.Failed : result;
    }
}
