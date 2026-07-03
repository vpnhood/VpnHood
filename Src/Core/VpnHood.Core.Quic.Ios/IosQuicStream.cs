using Foundation;
using Microsoft.Extensions.Logging;
using Network;
using System.Buffers;
using System.Runtime.InteropServices;
using VpnHood.Core.Toolkit.Logging;
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
    // ToDo: remove diagnose — bytes of this stream's in-flight (un-completed) native send, mirrored into
    // IosQuicClient.OutstandingSendBytes. Exchanged to 0 by whichever of completion/dispose runs first.
    private int _pendingSendBytes;

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
        // ToDo: remove diagnose
        _id = Interlocked.Increment(ref IosQuicClient.StreamSeq);
        var live = Interlocked.Increment(ref IosQuicClient.LiveStreamCount);
        VhLogger.Instance.LogInformation("[VHQUIC] +open id={Id} live={Live}", _id, live);
    }

    // JETSAM GUARD thresholds: at full download rate the per-packet native transients (NSData copies
    // retained briefly by NE/nw beyond our dispose) float several MB above baseline and their peaks
    // ratchet over the extension's 52 MB limit with no leak or backlog anywhere else (2026-07-01, third
    // crash flavor: no freeze, sendQ=0, pipeBuf=0, footprint oscillating 32->48 MB at 130 Mbps).
    // Braking the receive-arm — the single intake point of the whole download pipeline — while the
    // footprint is near the limit lets those transients drain; throughput only pays while within
    // ~7 MB of death. FootprintMb == 0 (probe absent) leaves the guard inactive.
    // 2026-07-02: brake earlier — the guarded crash showed a +6.6 MB spike inside ONE probe tick
    // (40.4 → 47.0), so by 45 the stale reading was already fatal. Start braking at 42.
    private const double GuardBrakeMb = 42.0;
    private const double GuardHardBrakeMb = 46.0;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<int>(cancellationToken);

        if (_disposed || _readEof || buffer.IsEmpty)
            return ValueTask.FromResult(0);

        // JETSAM GUARD: delay BEFORE arming the native receive so no new buffer is requested while
        // the footprint is critical. The guarded path is async (allocates a state machine) but only
        // runs while already near the limit; the normal path below stays allocation-free.
        var footprint = IosQuicClient.FootprintMb;
        if (footprint >= GuardBrakeMb)
            return ThrottledReadAsync(buffer, footprint, cancellationToken);

        return ArmReceive(buffer, cancellationToken);
    }

    private async ValueTask<int> ThrottledReadAsync(Memory<byte> buffer, double footprint, CancellationToken cancellationToken)
    {
        await Task.Delay(footprint >= GuardHardBrakeMb ? 100 : 25, cancellationToken).ConfigureAwait(false);
        if (_disposed || _readEof)
            return 0;
        return await ArmReceive(buffer, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<int> ArmReceive(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        _readSource.Reset();
        _readBuffer = buffer;

        if (cancellationToken.CanBeCanceled)
            _readReg = cancellationToken.Register(CancelReadCallback, _readSource);

        var maximumLength = (uint)Math.Min(buffer.Length, MaxReceiveLength);
        using var pool = new NSAutoreleasePool();
        _connection.ReceiveReadOnlyData(minimumIncompleteLength: 1, maximumLength: maximumLength, _readCallback);

        return new ValueTask<int>(_readSource, _readSource.Version);
    }

    private void OnReadCompleted(ReadOnlySpan<byte> data, NWContentContext? context, bool isComplete, NWError? error)
    {
        using var pool = new NSAutoreleasePool();
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

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        if (_disposed)
            return ValueTask.FromException(new ObjectDisposedException(nameof(IosQuicStream)));

        _writeSource.Reset();

        if (cancellationToken.CanBeCanceled)
            _writeReg = cancellationToken.Register(CancelWriteCallback, _writeSource);

        // ToDo: remove diagnose — count this send as in-flight until its completion callback fires.
        Interlocked.Exchange(ref _pendingSendBytes, buffer.Length);
        Interlocked.Add(ref IosQuicClient.OutstandingSendBytes, buffer.Length);

        using var pool = new NSAutoreleasePool();
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
        using var pool = new NSAutoreleasePool();
        _writeReg.Dispose();

        // ToDo: remove diagnose — the native send finished (or failed); it is no longer in flight.
        var pending = Interlocked.Exchange(ref _pendingSendBytes, 0);
        if (pending > 0)
            Interlocked.Add(ref IosQuicClient.OutstandingSendBytes, -pending);

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

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // ToDo: remove diagnose
        var live = Interlocked.Decrement(ref IosQuicClient.LiveStreamCount);
        VhLogger.Instance.LogInformation("[VHQUIC] -close id={Id} live={Live}", _id, live);

        if (disposing) {
            // ToDo: remove diagnose — time the native teardown; see IosQuicClient.MaxStreamCancelMs.
            var cancelStart = Environment.TickCount64;
            VhUtils.TryInvoke(() => _connection.Cancel());
            _connection.Dispose();
            var cancelMs = Environment.TickCount64 - cancelStart;
            long prevMax;
            while (cancelMs > (prevMax = Volatile.Read(ref IosQuicClient.MaxStreamCancelMs)) &&
                   Interlocked.CompareExchange(ref IosQuicClient.MaxStreamCancelMs, cancelMs, prevMax) != prevMax) { }

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

            // ToDo: remove diagnose — the completion may never fire after Cancel(); settle the counter.
            var pending = Interlocked.Exchange(ref _pendingSendBytes, 0);
            if (pending > 0)
                Interlocked.Add(ref IosQuicClient.OutstandingSendBytes, -pending);

            var rented = Interlocked.Exchange(ref _writeRentedArray, null);
            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
            _writeSource.TrySetResult();
        }
        base.Dispose(disposing);
    }
}
