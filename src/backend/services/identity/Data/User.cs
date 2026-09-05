namespace StreamForge.Identity.Api.Data;

/// <summary>Stores an account and its salted credential hash in the Identity boundary.</summary>
public sealed class User
{
    /// <summary>Gets or sets the immutable account identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the display username.</summary>
    public string Username { get; set; } = string.Empty;
    /// <summary>Gets or sets the case-normalized unique username.</summary>
    public string NormalizedUsername { get; set; } = string.Empty;
    /// <summary>Gets or sets the supplied email address.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Gets or sets the case-normalized unique email.</summary>
    public string NormalizedEmail { get; set; } = string.Empty;
    /// <summary>Gets or sets the versioned salted password hash; never serialize this entity.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>Gets or sets the server-generated creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
    /// <summary>Gets or sets the optional date of birth.</summary>
    public DateOnly? Dob { get; set; }
    /// <summary>Gets or sets the optional postal address.</summary>
    public string? Address { get; set; }
}
