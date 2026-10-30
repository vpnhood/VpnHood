using System.Buffers;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Core.Server.ProbeResistance;

// One buffer for the HTTP connection, including pipelined requests. The tunnel inherits this stream so
// bytes read past its headers are replayed. Ordinary ReadAsync never reads ahead after that handoff.
internal sealed class HttpReadStream(Stream sourceStream, ArrayPool<byte> bufferPool)
    : StreamDecoratorAsync(sourceStream, leaveOpen: true)
{
    private const int BufferSize = 16 * 1024;
    private readonly Lock _bufferLock = new();
    private byte[] _buffer = bufferPool.Rent(BufferSize);
    private int _offset;
    private int _count;
    private bool _filling;
    private bool _httpCompleted;

    public HttpReadStream(Stream sourceStream) : this(sourceStream, ArrayPool<byte>.Shared)
    {
    }

    // Like a Stream's reads, the HTTP parser's span and Consume calls have one reader.
    public ReadOnlySpan<byte> BufferedSpan => _buffer.AsSpan(_offset, _count);
    public override bool CanSeek => false;
    public override bool? DataAvailable => _count > 0 ? true : base.DataAvailable;

    public void Consume(int count)
    {
        lock (_bufferLock) {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _count);
            _offset += count;
            _count -= count;
            ReturnBufferIfUnused();
        }
    }

    // The tunnel consumes any read-ahead bytes, then owns only the source stream, not a 16 KB buffer.
    public void CompleteHttp()
    {
        lock (_bufferLock) {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_filling)
                throw new InvalidOperationException("An HTTP read is still in progress.");
            _httpCompleted = true;
            ReturnBufferIfUnused();
        }
    }

    public ValueTask<bool> EnsureBuffered(int minimum, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(minimum, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimum, BufferSize);
        lock (_bufferLock) {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_httpCompleted || _filling)
                throw new InvalidOperationException("HTTP buffering has ended or a read is already in progress.");
            if (_count >= minimum)
                return ValueTask.FromResult(true);
            _filling = true;
        }
        return Fill(minimum, cancellationToken);
    }

    private async ValueTask<bool> Fill(int minimum, CancellationToken cancellationToken)
    {
        // Dispose cannot return this array while the source has a pending write into it.
        try {
            if (_count > 0)
                _buffer.AsSpan(_offset, _count).CopyTo(_buffer);
            _offset = 0;
            while (_count < minimum) {
                var read = await SourceStream.ReadAsync(_buffer.AsMemory(_count, BufferSize - _count),
                    cancellationToken).Vhc();
                lock (_bufferLock) {
                    ObjectDisposedException.ThrowIf(IsDisposed, this);
                    if (read == 0)
                        return false;
                    _count += read;
                }
            }
            return true;
        }
        finally {
            lock (_bufferLock) {
                _filling = false;
                ReturnBufferIfUnused();
            }
        }
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_bufferLock) {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_filling)
                throw new InvalidOperationException("An HTTP read is still in progress.");
            if (_count > 0) {
                var count = Math.Min(buffer.Length, _count);
                BufferedSpan[..count].CopyTo(buffer.Span);
                Consume(count);
                return ValueTask.FromResult(count);
            }
        }
        return SourceStream.ReadAsync(buffer, cancellationToken);
    }

    private void ReturnBufferIfUnused()
    {
        if (_filling || _buffer.Length == 0 || (!IsDisposed && (!_httpCompleted || _count != 0)))
            return;

        var buffer = _buffer;
        _buffer = [];
        _offset = 0;
        _count = 0;
        bufferPool.Return(buffer, clearArray: true);
    }

    protected override void Dispose(bool disposing)
    {
        lock (_bufferLock) {
            base.Dispose(disposing);
            ReturnBufferIfUnused();
        }
    }
}
