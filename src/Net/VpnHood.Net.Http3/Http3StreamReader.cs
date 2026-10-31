using System.Buffers;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

// A bounded reader for the HTTP/3 front only. Authenticated tunnels bypass it completely.
internal sealed class Http3StreamReader(Stream stream) : IAsyncDisposable
{
    private const int BufferSize = 4096;
    private byte[]? _buffer;
    private int _offset;
    private int _count;
    private bool _endOfStream;
    private long _availableBytesRead;
    private Task<int>? _pendingRead;
    private CancellationTokenSource? _availableCancellation;
    private byte[] Buffer => _buffer ??= ArrayPool<byte>.Shared.Rent(BufferSize);
    public bool IsCritical { get; set; }
    public bool ReadAvailableOnly { get; set; }
    public long AvailableBytesLimit { get; set; } = long.MaxValue;
    public Action? OnReadPending { get; set; }

    public async ValueTask<int> ReadByte(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LimitAvailableRead(1);
        if (_count == 0 && !await Fill(cancellationToken).Vhc())
            return -1;
        var value = Buffer[_offset];
        Advance(1);
        return value;
    }

    public async ValueTask<long?> ReadIntegerOrEnd(CancellationToken cancellationToken)
    {
        var first = await ReadByte(cancellationToken).Vhc();
        if (first < 0)
            return null;
        var length = 1 << (first >> 6);
        long value = first & 0x3f;
        for (var i = 1; i < length; i++) {
            var next = await ReadByte(cancellationToken).Vhc();
            if (next < 0)
                throw new EndOfStreamException();
            value = (value << 8) | (byte)next;
        }
        return value;
    }

    public async ValueTask<long> ReadInteger(CancellationToken cancellationToken) =>
        await ReadIntegerOrEnd(cancellationToken).Vhc() ?? throw new EndOfStreamException();

    public async ValueTask<int> Read(Memory<byte> bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.IsEmpty)
            return 0;
        var requested = LimitAvailableRead(bytes.Length);
        if (_count == 0 && !await Fill(cancellationToken).Vhc())
            return 0;
        var count = Math.Min(requested, _count);
        Buffer.AsMemory(_offset, count).CopyTo(bytes);
        Advance(count);
        return count;
    }

    public async Task<byte[]> ReadPayload(long length, int limit, CancellationToken cancellationToken)
    {
        if (length > limit)
            throw new Http3ProtocolException(Http3Protocol.ExcessiveLoad);
        var bytes = new byte[(int)length];
        var offset = 0;
        while (offset < bytes.Length) {
            var count = await Read(bytes.AsMemory(offset), cancellationToken).Vhc();
            if (count == 0)
                throw new EndOfStreamException();
            offset += count;
        }
        return bytes;
    }

    public async Task Discard(long length, CancellationToken cancellationToken)
    {
        while (length > 0) {
            var count = await DiscardSome(length, cancellationToken).Vhc();
            if (count == 0)
                throw new EndOfStreamException();
            length -= count;
        }
    }

    public async ValueTask<int> DiscardSome(long maximumLength, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);
        if (maximumLength == 0)
            return 0;
        var requested = LimitAvailableRead((int)Math.Min(maximumLength, BufferSize));
        if (_count == 0 && !await Fill(cancellationToken).Vhc())
            return 0;
        var count = Math.Min(requested, _count);
        Advance(count);
        return count;
    }

    private int LimitAvailableRead(int requested)
    {
        if (!ReadAvailableOnly)
            return requested;
        var remaining = AvailableBytesLimit - _availableBytesRead;
        if (remaining <= 0)
            throw new Http3ReadBudgetExceededException();
        return (int)Math.Min(requested, remaining);
    }

    private void Advance(int count)
    {
        _offset += count;
        _count -= count;
        if (ReadAvailableOnly)
            _availableBytesRead += count;
    }

    private async ValueTask<bool> Fill(CancellationToken cancellationToken)
    {
        if (_endOfStream)
            return false;
        if (_pendingRead != null)
            throw new Http3ReadPendingException();

        // Available-only inspection may leave a native read pending after its caller moves on to the
        // response. Read exclusively into our private buffer, never a caller's temporary QPACK buffer.
        var read = stream.ReadAsync(Buffer.AsMemory(0, BufferSize), GetReadCancellationToken(cancellationToken));
        if (!read.IsCompleted) {
            // The control stream uses this boundary to finish already available frame validation
            // before acting on GOAWAY. The read stays owned here through cancellation and completion.
            OnReadPending?.Invoke();
            if (ReadAvailableOnly) {
                _pendingRead = read.AsTask();
                throw new Http3ReadPendingException();
            }
        }
        _count = await read.Vhc();
        _offset = 0;
        _endOfStream = _count == 0;
        return !_endOfStream;
    }

    private CancellationToken GetReadCancellationToken(CancellationToken cancellationToken) =>
        ReadAvailableOnly
            ? (_availableCancellation ??= CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)).Token
            : cancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_pendingRead != null) {
            try {
                if (_availableCancellation is { } cancellation)
                    await cancellation.CancelAsync().Vhc();
            }
            catch (Exception) {
                // Still observe the read below before releasing memory, even if cancellation fails.
            }
            try {
                await _pendingRead.Vhc();
            }
            catch (Exception) {
                // STOP_SENDING, cancellation, and transport shutdown all end this abandoned read.
            }
            _pendingRead = null;
        }
        _availableCancellation?.Dispose();
        _availableCancellation = null;
        if (_buffer != null) {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
    }
}
