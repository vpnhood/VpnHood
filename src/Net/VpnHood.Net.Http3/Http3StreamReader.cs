using System.Buffers;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

// A bounded reader for the HTTP/3 front only. Authenticated tunnels bypass it completely.
internal sealed class Http3StreamReader(Stream stream, bool allowBufferGrowth = false,
    IHttp3ReadPendingCallbacks? readPendingCallbacks = null, IQuicStream? quic = null) : IAsyncDisposable
{
    private const int BufferSize = 4096;
    private const int MaximumBufferSize = 16384;
    private bool _readFilledBuffer;
    private int _bufferSize = BufferSize;
    private byte[]? _buffer;
    private int _offset;
    private int _count;
    private bool _endOfStream;
    private long _availableBytesRead;
    private long _availableBytesLimit = long.MaxValue;
    private CancellationTokenSource? _readCancellation;
    private bool _readingStopped;
    private byte[] Buffer => _buffer ??= ArrayPool<byte>.Shared.Rent(BufferSize);
    public bool IsCritical { get; set; }

    public void BeginBodyInspection(long limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        if (_readCancellation != null)
            throw new InvalidOperationException("Body inspection has already started.");
        _availableBytesLimit = limit;
        _readCancellation = new CancellationTokenSource();
    }

    public void StopReading()
    {
        Volatile.Write(ref _readingStopped, true);
        _readCancellation?.TryCancel();
    }

    public void CompleteResponse()
    {
        var quicStream = quic ?? throw new InvalidOperationException("The reader has no native stream control.");
        // Native abort can resume the parser inline. Publish completion first, then send H3_NO_ERROR
        // before token cancellation can make the transport use its default reset code.
        Volatile.Write(ref _readingStopped, true);
        quicStream.AbortRead(Http3Protocol.NoError);
        _readCancellation?.TryCancel();
    }

    public ValueTask<int> ReadByte(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LimitAvailableRead(1);
        if (_count == 0)
            return ReadByteAfterFill(cancellationToken);
        var value = Buffer[_offset];
        Advance(1);
        return ValueTask.FromResult((int)value);
    }

    private async ValueTask<int> ReadByteAfterFill(CancellationToken cancellationToken)
    {
        if (!await Fill(cancellationToken).Vhc())
            return -1;
        return await ReadByte(cancellationToken).Vhc();
    }

    public bool TryReadBuffered(int count, out ReadOnlyMemory<byte> bytes)
    {
        if (count > _count || LimitAvailableRead(count) < count) {
            bytes = default;
            return false;
        }
        bytes = Buffer.AsMemory(_offset, count);
        Advance(count);
        return true;
    }

    public ReadOnlySpan<byte> GetBufferedBytes(int maximumLength)
    {
        var count = Math.Min(_count, maximumLength);
        return count == 0 ? [] : Buffer.AsSpan(_offset, LimitAvailableRead(count));
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
        if (_availableBytesLimit == long.MaxValue)
            return requested;
        var remaining = _availableBytesLimit - _availableBytesRead;
        if (remaining <= 0)
            throw new Http3ReadBudgetExceededException();
        return (int)Math.Min(requested, remaining);
    }

    private void Advance(int count)
    {
        _offset += count;
        _count -= count;
        if (_availableBytesLimit != long.MaxValue)
            _availableBytesRead += count;
    }

    private async ValueTask<bool> Fill(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _readingStopped))
            throw new Http3InspectionCompleteException();
        if (_endOfStream)
            return false;
        // Dispatch reports the protocol error before StopReading cancels native input; linking the
        // deadline here could let a transport's default cancellation reset win that race.
        var readToken = _readCancellation?.Token ?? cancellationToken;
        var buffer = Buffer;
        if (allowBufferGrowth && _readFilledBuffer && _bufferSize < MaximumBufferSize) {
            // Only requests grow after a consumed full read. Long-lived control/QPACK streams keep
            // 4 KB even after large bursts; frame declarations alone never grow the buffer.
            _buffer = ArrayPool<byte>.Shared.Rent(MaximumBufferSize);
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = _buffer;
            _bufferSize = MaximumBufferSize;
        }
        try {
            var read = stream.ReadAsync(buffer.AsMemory(0, _bufferSize), readToken);
            if (!read.IsCompleted) {
                // The control stream uses this boundary to finish already available frame validation
                // before acting on GOAWAY. The read stays owned here through cancellation and completion.
                readPendingCallbacks?.OnReadPending(this);
            }
            _count = await read.Vhc();
        }
        catch (Exception) {
            if (Volatile.Read(ref _readingStopped) && !cancellationToken.IsCancellationRequested)
                throw new Http3InspectionCompleteException();
            throw;
        }
        _offset = 0;
        _readFilledBuffer = _count == _bufferSize;
        _endOfStream = _count == 0;
        return !_endOfStream;
    }

    public ValueTask DisposeAsync()
    {
        _readCancellation?.Dispose();
        _readCancellation = null;
        if (_buffer != null) {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
        return ValueTask.CompletedTask;
    }
}
