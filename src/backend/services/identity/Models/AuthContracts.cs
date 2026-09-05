using System.ComponentModel.DataAnnotations;

namespace StreamForge.Identity.Api.Models;

/// <summary>Supplies new-account credentials and optional profile fields.</summary>
public sealed record RegisterRequest(
    [Required, StringLength(50, MinimumLength = 3), RegularExpression(@"^[\p{L}\p{Nd}_.-]+$")] string Username,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 15)] string Password,
    DateOnly? Dob,
    [StringLength(1000)] string? Address);

/// <summary>Supplies email credentials without normalizing the password.</summary>
public sealed record LoginRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 15)] string Password);

/// <summary>Contains the account fields safe to return to its signed-in browser.</summary>
public sealed record UserResponse(Guid Id, string Username, string Email);

/// <summary>Returns current account state and absolute expiry without a session identifier.</summary>
public sealed record AuthResponse(UserResponse User, DateTimeOffset ExpiresAtUtc);
