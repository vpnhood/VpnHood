using Foundation;
using Network;
using System.Buffers;
using System.Runtime.InteropServices;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// Adapts a single QUIC stream (an <see cref="NWConnection"/> extracted from a multiplexed QUIC tunnel)
/// to a <see cref="Stream"/>, the surface VpnHood's tunneling code consumes.
/// </summary>
/// <remarks>
/// Network.framework send/receive are callback based. Each <see cref="ReadAsync(Memory{byte},CancellationToken)"/>
/// arms exactly one native receive sized to the caller's buffer and copies the bytes straight in — no
/// intermediate buffer and no read-ahead, so an idle stream holds no pending native receive. Both read and
/// write are bridged to reusable <see cref="ReusableValueTaskSource"/> instances to avoid per-call
/// allocations. Sync I/O is unsupported (inherited from <see cref="AsyncStream"/>).
/// </remarks>
internal sealed class IosQuicStream : AsyncStream
{
    private readonly NWConnection _connection;
    private readonly int _id;
    private bool _disposed;

    private readonly NWConnectionReceiveReadOnlySpanCompletion _readCallback;
    private readonly Action<NWError?> _writeCallback;

    private readonly ReusableValueTaskSource<int> _readSource = new();
    private CancellationTokenRegistration _readReg;
    private Memory<byte> _readBuffer;
    private bool _readEof;

    private readonly ReusableValueTaskSource _writeSource = new();
    private CancellationTokenRegistration _writeReg;
    private byte[]? _writeRentedArray;

    // Cap a single native receive so download bursts buffer in NATIVE memory in bounded chunks even if a
    // caller hands us a very large buffer. The QUIC flow-control window (IosQuicClient) is the real ceiling.
    private const int MaxReceiveLength = 16 * 1024;

    private static readonly Action<object?> CancelReadCallback = (state) => {
        ((ReusableValueTaskSource<int>?)state)?.TrySetException(new OperationCanceledException());
    };

    private static readonly Action<object?> CancelWriteCallback = (state) => {
        ((ReusableValueTaskSource?)state)?.TrySetException(new OperationCanceledException());
    };

    public override bool CanRead => !_disposed;
    public override bool CanWrite => !_disposed;

    public IosQuicStream(NWConnection connection) : base()
    {
        _connection = connection;
        _readCallback = OnReadCompleted;
        _writeCallback = OnWriteCompleted;
        _id = Interlocked.Increment(ref IosQuicClient.StreamSeq);
        var live = Interlocked.Increment(ref IosQuicClient.LiveStreamCount);
        IosQuicClient.DeviceLog($"[VHQUIC] +open id={_id} live={live}");
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<int>(cancellationToken);

        if (_disposed || _readEof || buffer.IsEmpty)
            return ValueTask.FromResult(0);

        _readSource.Reset();
        _readBuffer = buffer;

        if (cancellationToken.CanBeCanceled)
            _readReg = cancellationToken.Register(CancelReadCallback, _readSource);

        var maximumLength = (uint)Math.Min(buffer.Length, MaxReceiveLength);
        using (new NSAutoreleasePool()) {
            _connection.ReceiveReadOnlyData(minimumIncompleteLength: 1, maximumLength: maximumLength, _readCallback);
        }

        return new ValueTask<int>(_readSource, _readSource.Version);
    }

    private void OnReadCompleted(ReadOnlySpan<byte> data, NWContentContext? context, bool isComplete, NWError? error)
    {
        using (new NSAutoreleasePool()) {
            _readReg.Dispose();

            var buffer = _readBuffer;
            _readBuffer = default;

            try {
                // Claim completion BEFORE touching the buffer. If cancellation or Dispose already completed
                // this read, the caller's buffer may have been returned to the shared pool (and re-rented by
                // another channel) — copying native data into it would corrupt unrelated memory. Reserving
                // first also keeps the buffer alive: the consumer's read can't unwind until we publish.
                if (!_readSource.TryReserve())
                    return;

                if (error != null) {
                    _readSource.SetReservedException(new IOException($"QUIC stream receive failed: {error}"));
                    return;
                }

                if (isComplete)
                    _readEof = true;

                // Empty completion is EOF (n == 0). A non-empty final chunk (isComplete == true) is still
                // returned here; the NEXT read arms a receive that returns empty -> 0, i.e. standard EOF.
                var n = 0;
                if (!data.IsEmpty && !buffer.IsEmpty) {
                    n = Math.Min(data.Length, buffer.Length);
                    data[..n].CopyTo(buffer.Span);
                }
                _readSource.SetReservedResult(n);
            }
            finally {
                context?.Dispose();
                error?.Dispose();
            }
        }
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        if (_disposed)
            return ValueTask.FromException(new ObjectDisposedException(nameof(IosQuicStream)));

        _writeSource.Reset();

        if (cancellationToken.CanBeCanceled) {
            _writeReg = cancellationToken.Register(CancelWriteCallback, _writeSource);
        }

        using (new NSAutoreleasePool()) {
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
        }

        return new ValueTask(_writeSource, _writeSource.Version);
    }

    private void OnWriteCompleted(NWError? error)
    {
        using (new NSAutoreleasePool()) {
            _writeReg.Dispose();

            var rented = Interlocked.Exchange(ref _writeRentedArray, null);
            if (rented != null) {
                ArrayPool<byte>.Shared.Return(rented);
            }

            if (error != null) {
                _writeSource.TrySetException(new IOException($"QUIC stream send failed: {error}"));
                error.Dispose();
            }
            else {
                _writeSource.TrySetResult();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        var live = Interlocked.Decrement(ref IosQuicClient.LiveStreamCount);
        IosQuicClient.DeviceLog($"[VHQUIC] -close id={_id} live={live}");

        if (disposing) {
            VhUtils.TryInvoke(() => _connection.Cancel());
            _connection.Dispose();

            // Dispose the pending cancellation registrations (the native callbacks that normally dispose them
            // may never fire after Cancel()). Safe no-ops if default/already disposed.
            _readReg.Dispose();
            _writeReg.Dispose();

            // Unblock any pending read/write and reclaim an in-flight write's rented buffer. The native
            // receive/send completions may also fire later (or be dropped entirely after Cancel()); the
            // value-task source's state guard makes a second completion a no-op, and Interlocked.Exchange
            // makes the buffer return idempotent. Done after Cancel()/Dispose() so the native send is no
            // longer reading the array.
            _readSource.TrySetResult(0);

            var rented = Interlocked.Exchange(ref _writeRentedArray, null);
            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
            _writeSource.TrySetResult();
        }
        base.Dispose(disposing);
    }
}
