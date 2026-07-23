using CoreFoundation;
using Network;
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
/// intermediate buffer and no read-ahead, so an idle stream holds no pending native receive. Read/write
/// callbacks carry per-operation state so late native completions cannot complete a later operation. Sync I/O
/// is unsupported (inherited from <see cref="AsyncStream"/>).
/// </remarks>
internal sealed class IosQuicStream(NWConnection connection) : AsyncStream
{
    // Stream lifecycle instrumentation (diagnostics only). Paired with OnStreamClosed in Dispose.
    private readonly int _id = IosQuicDiagnostics.OnStreamOpened();
    private bool _disposed;
    private bool _aborted;

    private IosQuicStreamReadOperation? _activeRead;
    private bool _readEof;

    private IosQuicStreamWriteOperation? _activeWrite;

    // Cap a single native receive so download bursts buffer in NATIVE memory in bounded chunks even if a
    // caller hands us a very large buffer. The QUIC flow-control window (IosQuicClient) is the real ceiling.
    private const int MaxReceiveLength = 16 * 1024;

    private static readonly Action<object?> CancelReadCallback = static state => {
        var op = (IosQuicStreamReadOperation)state!;
        op.Owner?.CancelRead(op);
    };

    private static readonly Action<object?> CancelWriteCallback = static state => {
        var op = (IosQuicStreamWriteOperation)state!;
        op.Owner?.CancelWrite(op);
    };

    public override bool CanRead => !_disposed && !_aborted;
    public override bool CanWrite => !_disposed && !_aborted;

    // JETSAM GUARD history: at full download rate the per-packet native transients (NSData copies
    // retained briefly by NE/nw beyond our dispose) float several MB above baseline and their peaks
    // ratchet over the extension's 52 MB limit with no leak or backlog anywhere else (2026-07-01, third
    // crash flavor: no freeze, sendQ=0, pipeBuf=0, footprint oscillating 32->48 MB at 130 Mbps).
    // Stopping the receive-arm — the single intake point of the whole download pipeline — while the
    // footprint is near the limit lets those transients drain; throughput only pays while within
    // ~7 MB of death. 2026-07-11: the per-stream 25/100 ms delay brake that lived here did NOT scale —
    // 40+ streams delaying independently still admitted ~28 MB/s, and it fired once in 134 s while the
    // footprint ratcheted 42 → 49.8 into a jetsam kill — replaced by the GLOBAL IosQuicIngestGate
    // (shared close/reopen thresholds with hysteresis; braking strength independent of stream count).

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<int>(cancellationToken);

        if (_disposed || _readEof || buffer.IsEmpty)
            return ValueTask.FromResult(0);

        if (_aborted)
            return ValueTask.FromException<int>(new ObjectDisposedException(nameof(IosQuicStream)));

        // DO NOT throttle or block reads to save memory — device-measured 2026-07-12 (REGION-CENSUS):
        // Network.framework grants QUIC flow-control credit as IT buffers (not as the app reads), so
        // inbound data piles up in NATIVE malloc_small whenever reads lag the wire rate, and blocking
        // reads (the old 25/100 ms brake, then a global footprint gate) LOCKS that hoard in — the only
        // thing that shrinks it is reading it out. Footprint pressure is handled where the memory truly
        // lives instead: the connection pool disposes jammed connections immediately and panic-recycles
        // all connections near the platform kill limit (QuicStreamConnectionFactory).
        return ArmReceive(buffer, cancellationToken);
    }

    private ValueTask<int> ArmReceive(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var op = new IosQuicStreamReadOperation(this, buffer, cancellationToken);
        if (Interlocked.CompareExchange(ref _activeRead, op, null) != null)
            return ValueTask.FromException<int>(
                new InvalidOperationException("A read operation is already pending on this QUIC stream."));

        try {
            if (cancellationToken.CanBeCanceled)
                op.Registration = cancellationToken.Register(CancelReadCallback, op);

            var maximumLength = (uint)Math.Min(buffer.Length, MaxReceiveLength);
            using var pool = new NSAutoreleasePool();
            connection.ReceiveData(minimumIncompleteLength: 1, maximumLength: maximumLength, op.Callback);
        }
        catch (Exception ex) {
            Interlocked.CompareExchange(ref _activeRead, null, op);
            op.Registration.Dispose();
            op.Source.TrySetException(ex);
            return new ValueTask<int>(op.Source, op.Source.Version);
        }

        return new ValueTask<int>(op.Source, op.Source.Version);
    }

    internal void OnReadCompleted(
        IosQuicStreamReadOperation op, DispatchData? data, NWContentContext? context, bool isComplete, NWError? error)
    {
        using var pool = new NSAutoreleasePool();
        Interlocked.CompareExchange(ref _activeRead, null, op);
        op.Registration.Dispose();

        try {
            // Claim completion BEFORE touching the buffer. If cancellation or Dispose already completed
            // this read, the caller's buffer may have been returned to the shared pool (and re-rented by
            // another channel) — copying native data into it would corrupt unrelated memory. Reserving
            // first also keeps the buffer alive: the consumer's read can't unwind until we publish.
            if (!op.Source.TryReserve())
                return;

            if (error != null) {
                op.Source.SetReservedException(new IOException($"QUIC stream receive failed: {error}"));
                return;
            }

            if (isComplete)
                _readEof = true;

            // Empty completion is EOF (n == 0). A non-empty final chunk (isComplete == true) is still
            // returned here; the NEXT read arms a receive that returns empty -> 0, i.e. standard EOF.
            var n = 0;
            var buffer = op.Buffer;
            if (data != null && data.Size > 0 && !buffer.IsEmpty) {
                using var mappedData = data.CreateMap(out var dataPointer, out var dataLength);
                n = Math.Min(checked((int)dataLength), buffer.Length);
                unsafe {
                    new ReadOnlySpan<byte>((void*)dataPointer, n).CopyTo(buffer.Span);
                }
            }
            op.Source.SetReservedResult(n);
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

        if (_disposed || _aborted)
            return ValueTask.FromException(new ObjectDisposedException(nameof(IosQuicStream)));

        if (buffer.IsEmpty)
            return ValueTask.CompletedTask;

        var op = new IosQuicStreamWriteOperation(this, buffer.Length, cancellationToken);
        if (Interlocked.CompareExchange(ref _activeWrite, op, null) != null) {
            op.DisposeOutstandingSend();
            return ValueTask.FromException(
                new InvalidOperationException("A write operation is already pending on this QUIC stream."));
        }

        try {
            if (cancellationToken.CanBeCanceled)
                op.Registration = cancellationToken.Register(CancelWriteCallback, op);

            if (_disposed || _aborted || !ReferenceEquals(Volatile.Read(ref _activeWrite), op)) {
                Interlocked.CompareExchange(ref _activeWrite, null, op);
                op.Registration.Dispose();
                op.DisposeOutstandingSend();
                op.Source.TrySetException(new ObjectDisposedException(nameof(IosQuicStream)));
                return new ValueTask(op.Source, op.Source.Version);
            }

            using var pool = new NSAutoreleasePool();
            // DispatchData copies the span into native-owned storage. Network.framework retains that
            // storage for the asynchronous send, so release our wrapper immediately after Send returns.
            using var dispatchData = DispatchData.FromReadOnlySpan(buffer.Span);
            connection.Send(dispatchData, NWContentContext.DefaultMessage, isComplete: false, op.Callback);
        }
        catch (Exception ex) {
            Interlocked.CompareExchange(ref _activeWrite, null, op);
            op.Registration.Dispose();
            op.DisposeOutstandingSend();

            op.Source.TrySetException(ex);
            return new ValueTask(op.Source, op.Source.Version);
        }

        return new ValueTask(op.Source, op.Source.Version);
    }

    internal void OnWriteCompleted(IosQuicStreamWriteOperation op, NWError? error)
    {
        using var pool = new NSAutoreleasePool();
        Interlocked.CompareExchange(ref _activeWrite, null, op);
        op.Registration.Dispose();

        // The native send finished (or failed); it is no longer in flight. Settles the diagnostic
        // in-flight counter (no-op unless IosQuicDiagnostics.Enabled).
        op.DisposeOutstandingSend();

        if (error != null) {
            op.Source.TrySetException(new IOException($"QUIC stream send failed: {error}"));
            error.Dispose();
        }
        else {
            op.Source.TrySetResult();
        }
    }

    private void CancelRead(IosQuicStreamReadOperation op)
    {
        if (!ReferenceEquals(Volatile.Read(ref _activeRead), op))
            return;

        op.Source.TrySetException(new OperationCanceledException(op.CancellationToken));
        AbortNativeConnection();
    }

    private void CancelWrite(IosQuicStreamWriteOperation op)
    {
        if (!ReferenceEquals(Volatile.Read(ref _activeWrite), op))
            return;

        op.Source.TrySetException(new OperationCanceledException(op.CancellationToken));
        AbortNativeConnection();
    }

    private void AbortNativeConnection()
    {
        if (Interlocked.Exchange(ref _aborted, true))
            return;

        VhUtils.TryInvoke(connection.Cancel);
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // Stream lifecycle instrumentation (diagnostics only). Paired with OnStreamOpened in the ctor.
        IosQuicDiagnostics.OnStreamClosed(_id);
        Toolkit.Memory.VhTypeTracker.Record("IosQuicStream.disposed");

        if (disposing) {
            // Time the native teardown (diagnostics only); the Cancel()/Dispose() themselves are
            // load-bearing and run unconditionally.
            var cancelStart = IosQuicDiagnostics.BeginTiming();
            // ReSharper disable once AccessToDisposedClosure
            VhUtils.TryInvoke(connection.Cancel);
            connection.Dispose();
            IosQuicDiagnostics.EndStreamTeardown(cancelStart);

            // Dispose the pending cancellation registrations (the native callbacks that normally dispose them
            // may never fire after Cancel()). Safe no-ops if default/already disposed.
            var readOp = Interlocked.Exchange(ref _activeRead, null);
            readOp?.Registration.Dispose();
            readOp?.Clear();

            var writeOp = Interlocked.Exchange(ref _activeWrite, null);
            writeOp?.Registration.Dispose();

            // Unblock any pending read/write and reclaim an in-flight rented buffer of write. The native
            // receive/send completions may also fire later (or be dropped entirely after Cancel()); the
            // value-task source's state guard makes a second completion a no-op, and Interlocked.Exchange
            // makes the buffer return idempotent. Done after Cancel()/Dispose() so the native send is no
            // longer reading the array.
            readOp?.Source.TrySetResult(0);

            // The completion may never fire after Cancel(); settle the diagnostic in-flight counter
            // (no-op unless IosQuicDiagnostics.Enabled).
            writeOp?.DisposeOutstandingSend();
            writeOp?.Clear();
            writeOp?.Source.TrySetException(new ObjectDisposedException(nameof(IosQuicStream)));
        }
        base.Dispose(disposing);
    }

}
