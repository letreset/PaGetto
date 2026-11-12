using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// Fails once more than a number of bytes were read from the inner stream. Package archives are
/// read through it, because a small upload can decompress to a huge archive.
/// </summary>
internal sealed class LimitedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly Func<Exception> _createLimitException;
    private long _remaining;

    /// <param name="inner">The stream to read from. It is not disposed.</param>
    /// <param name="limit">The number of bytes that may be read.</param>
    /// <param name="createLimitException">Creates the exception thrown once the limit is passed.</param>
    public LimitedReadStream(Stream inner, long limit, Func<Exception> createLimitException)
    {
        _inner = inner;
        _remaining = limit;
        _createLimitException = createLimitException;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Count(_inner.Read(buffer, offset, count));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return Count(await _inner.ReadAsync(buffer, cancellationToken));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    private int Count(int read)
    {
        _remaining -= read;
        if (_remaining < 0)
        {
            throw _createLimitException();
        }

        return read;
    }
}
