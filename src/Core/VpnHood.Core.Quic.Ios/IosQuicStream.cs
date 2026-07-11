using Network;
using System.Buffers;
using System.Runtime.InteropServices;
using VpnHood.Core.Toolkit.Memory;
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
        op.Owner.CancelRead(op);
    };

    private static readonly Action<object?> CancelWriteCallback = static state => {
        var op = (IosQuicStreamWriteOperation)state!;
        op.Owner.CancelWrite(op);
    };

    public override bool CanRead => !_disposed && !_aborted;
    public override bool CanWrite => !_disposed && !_aborted;

    // JETSAM GUARD thresholds: at full download rate the per-packet native transients (NSData copies
    // retained briefly by NE/nw beyond our dispose) float several MB above baseline and their peaks
    // ratchet over the extension's 52 MB limit with no leak or backlog anywhere else (2026-07-01, third
    // crash flavor: no freeze, sendQ=0, pipeBuf=0, footprint oscillating 32->48 MB at 130 Mbps).
    // Braking the receive-arm — the single intake point of the whole download pipeline — while the
    // footprint is near the limit lets those transients drain; throughput only pays while within
    // ~7 MB of death. No memory reader installed (VhMemory.Instance) reports 0 -> guard inactive.
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

        if (_aborted)
            return ValueTask.FromException<int>(new ObjectDisposedException(nameof(IosQuicStream)));

        // JETSAM GUARD: delay BEFORE arming the native receive so no new buffer is requested while
        // the footprint is critical. The guarded path only runs while already near the limit. The value is a
        // live phys_footprint read via the ambient VhMemory.Instance; no reader installed -> 0 -> inactive.
        var footprint = VhMemory.Instance.GetInfo().ProcessFootprintMb ?? 0;
        if (footprint >= GuardBrakeMb)
            return ThrottledReadAsync(buffer, footprint, cancellationToken);

        return ArmReceive(buffer, cancellationToken);
    }

    private async ValueTask<int> ThrottledReadAsync(Memory<byte> buffer, double footprint, CancellationToken cancellationToken)
    {
        var hard = footprint >= GuardHardBrakeMb;
        // Diagnostics only: counted always, but self-throttled to ~1 summary line/sec (no-op in production).
        IosQuicDiagnostics.TraceBrake(footprint, hard);
        await Task.Delay(hard ? 100 : 25, cancellationToken).Vhc();
        if (_disposed || _readEof)
            return 0;
        if (_aborted)
            throw new ObjectDisposedException(nameof(IosQuicStream));
        return await ArmReceive(buffer, cancellationToken).Vhc();
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
            connection.ReceiveReadOnlyData(minimumIncompleteLength: 1, maximumLength: maximumLength, op.Callback);
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
        IosQuicStreamReadOperation op, ReadOnlySpan<byte> data, NWContentContext? context, bool isComplete, NWError? error)
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
            if (!data.IsEmpty && !buffer.IsEmpty) {
                n = Math.Min(data.Length, buffer.Length);
                data[..n].CopyTo(buffer.Span);
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
            if (MemoryMarshal.TryGetArray(buffer, out var segment)) {
                // isComplete: false -> more data may follow on this stream (do not signal FIN).
                connection.Send(segment.Array!, segment.Offset, segment.Count, NWContentContext.DefaultMessage,
                    isComplete: false, op.Callback);
            }
            else {
                var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
                try {
                    buffer.CopyTo(rented);
                    connection.Send(rented, 0, buffer.Length, NWContentContext.DefaultMessage,
                        isComplete: false, op.Callback);
                }
                finally {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
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
            writeOp?.Source.TrySetException(new ObjectDisposedException(nameof(IosQuicStream)));
        }
        base.Dispose(disposing);
    }

}
