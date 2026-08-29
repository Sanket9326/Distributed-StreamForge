namespace StreamForge.Upload.Api.Services;

/// <summary>
/// Wraps a forward-only upload stream, counts consumed bytes, and rejects content beyond a configured limit.
/// </summary>
public sealed class SizeLimitedReadStream(Stream inner, long maximumBytes) : Stream
{
    private long bytesRead;

    /// <summary>Gets the number of source bytes consumed through this stream.</summary>
    public long BytesRead => bytesRead;

    /// <inheritdoc />
    public override bool CanRead => inner.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => bytesRead;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        var permittedCount = GetPermittedCount(count);
        var read = inner.Read(buffer, offset, permittedCount);
        TrackRead(read);
        return read;
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var permittedCount = GetPermittedCount(buffer.Length);
        var read = inner.Read(buffer[..permittedCount]);
        TrackRead(read);
        return read;
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var permittedCount = GetPermittedCount(buffer.Length);
        var read = await inner.ReadAsync(buffer[..permittedCount], cancellationToken);
        TrackRead(read);
        return read;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override void Flush() => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private int GetPermittedCount(int requestedCount)
    {
        var remaining = maximumBytes - bytesRead;
        return (int)Math.Min(requestedCount, Math.Max(1, remaining + 1));
    }

    private void TrackRead(int count)
    {
        bytesRead += count;
        if (bytesRead > maximumBytes)
        {
            throw new UploadRequestException(
                StatusCodes.Status413PayloadTooLarge,
                "Video is too large",
                $"The video cannot exceed {maximumBytes} bytes.");
        }
    }
}
