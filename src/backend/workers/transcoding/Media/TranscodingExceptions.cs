namespace StreamForge.Transcoding.Worker.Media;

/// <summary>Represents a source or contract failure that retries cannot correct.</summary>
public sealed class PermanentTranscodingException(string code, string safeMessage, Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    public string Code { get; } = code;

    public string SafeMessage { get; } = safeMessage;
}

/// <summary>Represents an infrastructure or process failure that may recover.</summary>
public sealed class TransientTranscodingException(string code, string safeMessage, Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    public string Code { get; } = code;

    public string SafeMessage { get; } = safeMessage;
}
