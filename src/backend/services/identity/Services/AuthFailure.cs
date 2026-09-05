namespace StreamForge.Identity.Api.Services;

/// <summary>Represents an expected authentication failure safe to expose as Problem Details.</summary>
public sealed class AuthFailure(int status, string code, string message, int? retryAfterSeconds = null) : Exception(message)
{
    /// <summary>Gets the HTTP response status.</summary>
    public int Status { get; } = status;
    /// <summary>Gets the stable machine-readable error code.</summary>
    public string Code { get; } = code;
    /// <summary>Gets the delay before a rate-limited caller should retry.</summary>
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
