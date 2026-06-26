using Network;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// Adapts a single QUIC stream (an <see cref="NWConnection"/> extracted from a multiplexed QUIC tunnel)
/// to a <see cref="Stream"/>, the surface VpnHood's tunneling code consumes.
/// </summary>
/// <remarks>
/// Network.framework send/receive are callback based; each call is bridged to a
/// <see cref="IValueTaskSource"/>. Reads use the read-only-span receive overload so the native
/// buffer is copied synchronously inside the callback (the span must not escape it).
/// </remarks>
internal sealed class IosQuicStream : Stream
{
    private readonly NWConnection _connection;
    private bool _disposed;

    private readonly NWConnectionReceiveReadOnlySpanCompletion _readCallback;
    private readonly Action<NWError?> _writeCallback;

    private Memory<byte> _readBuffer;
    private readonly ReadValueTaskSource _readSource = new();
    private readonly WriteValueTaskSource _writeSource = new();

    private CancellationTokenRegistration _readReg;
    private CancellationTokenRegistration _writeReg;
    private byte[]? _writeRentedArray;

    private static readonly Action<object?> CancelReadCallback = (state) => {
        ((ReadValueTaskSource?)state)?.TrySetException(new OperationCanceledException());
    };

    private static readonly Action<object?> CancelWriteCallback = (state) => {
        ((WriteValueTaskSource?)state)?.TrySetException(new OperationCanceledException());
    };

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public IosQuicStream(NWConnection connection) : base()
    {
        _connection = connection;
        _readCallback = OnReadCompleted;
        _writeCallback = OnWriteCompleted;
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<int>(cancellationToken);

        _readSource.Reset();
        _readBuffer = buffer;

        if (cancellationToken.CanBeCanceled) {
            _readReg = cancellationToken.Register(CancelReadCallback, _readSource);
        }

        // minimumIncompleteLength: 1 -> return as soon as any data is available (stream semantics).
        _connection.ReceiveReadOnlyData(minimumIncompleteLength: 1, maximumLength: (uint)buffer.Length, _readCallback);

        return new ValueTask<int>(_readSource, _readSource.Version);
    }

    private void OnReadCompleted(ReadOnlySpan<byte> data, NWContentContext? context, bool isComplete, NWError? error)
    {
        _readReg.Dispose();

        var buffer = _readBuffer;
        _readBuffer = default;

        if (error != null) {
            _readSource.TrySetException(new IOException($"QUIC stream receive failed: {error}"));
            return;
        }

        if (!data.IsEmpty && !buffer.IsEmpty)
            data.CopyTo(buffer.Span);

        _readSource.TrySetResult(data.Length);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        _writeSource.Reset();

        if (cancellationToken.CanBeCanceled) {
            _writeReg = cancellationToken.Register(CancelWriteCallback, _writeSource);
        }

        if (MemoryMarshal.TryGetArray(buffer, out var segment)) {
            // isComplete: false -> more data may follow on this stream (do not signal FIN).
            _connection.Send(segment.Array!, segment.Offset, segment.Count, NWContentContext.DefaultMessage, isComplete: false, _writeCallback);
        }
        else {
            var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
            _writeRentedArray = rented;
            buffer.CopyTo(rented);
            _connection.Send(rented, 0, buffer.Length, NWContentContext.DefaultMessage, isComplete: false, _writeCallback);
        }

        return new ValueTask(_writeSource, _writeSource.Version);
    }

    private void OnWriteCompleted(NWError? error)
    {
        _writeReg.Dispose();

        var rented = Interlocked.Exchange(ref _writeRentedArray, null);
        if (rented != null) {
            ArrayPool<byte>.Shared.Return(rented);
        }

        if (error != null)
            _writeSource.TrySetException(new IOException($"QUIC stream send failed: {error}"));
        else
            _writeSource.TrySetResult();
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(new Memory<byte>(buffer, offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        if (disposing) {
            try { _connection.Cancel(); } catch { /* ignore */ }
            _connection.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class ReadValueTaskSource : IValueTaskSource<int>
    {
        private ManualResetValueTaskSourceCore<int> _core;
        private int _state;

        public short Version => _core.Version;

        public void Reset()
        {
            _state = 0;
            _core.Reset();
        }

        public bool TrySetResult(int result)
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0) {
                _core.SetResult(result);
                return true;
            }
            return false;
        }

        public bool TrySetException(Exception ex)
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0) {
                _core.SetException(ex);
                return true;
            }
            return false;
        }

        public int GetResult(short token) => _core.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _core.OnCompleted(continuation, state, token, flags);
    }

    private sealed class WriteValueTaskSource : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> _core;
        private int _state;

        public short Version => _core.Version;

        public void Reset()
        {
            _state = 0;
            _core.Reset();
        }

        public bool TrySetResult()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0) {
                _core.SetResult(true);
                return true;
            }
            return false;
        }

        public bool TrySetException(Exception ex)
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0) {
                _core.SetException(ex);
                return true;
            }
            return false;
        }

        public void GetResult(short token) => _core.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _core.OnCompleted(continuation, state, token, flags);
    }
}
