namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Represents a MinIO or object-storage transport failure that should be exposed as dependency unavailability.
/// </summary>
public sealed class ObjectStorageException(string message, Exception innerException)
    : Exception(message, innerException);
