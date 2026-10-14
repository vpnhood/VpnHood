using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.TcpStack.Primitives;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Memory;
using VpnHood.Net.Toolkit.Net;

// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo

namespace VpnHood.Net.TcpStack;

internal sealed class LocalTcpConnection(
    IPEndPointPairValue ipEndPointPair,
    uint isnLocal,
    uint isnRemote,
    ushort? peerMss,
    LocalTcpListener listener,
    byte peerWsShift,
    LocalTcpStackOptions options,
    PipeOptions pipeOptions,
    TcpStackDiagnostics diagnostics)
    : IDisposable
{
    // All memory/timeout sizing comes from the stack's validated options (see LocalTcpStackOptions).
    // The values used on the data path below are cached into readonly fields once, here: the hot path
    // (send/recv/ACK) pays a field read, no recompute.

    private readonly TimeSpan _idleTimeout = options.IdleTimeout;

    // The peer's bytes waiting for the app and the window advertised for them; HandleData bounds every write by its
    // Available, so ReceiveWindowSize is a hard cap on the per-connection receive memory even against a peer that ignores it.
    private readonly ReceiveWindow _receiveWindow = new(options, pipeOptions, ClampMss(peerMss, options));

    private readonly Lock _seqLock = new();
    private readonly SemaphoreSlim _windowSignal = new(0, 1);
    private LocalTcpClient? _pendingClient;
    private LocalTcpStack? _stack;

    private bool _finSent;
    private bool _finReceived;
    private bool _readSideClosed; // the app closed its stream: new bytes from the peer are reset, not buffered
    private bool _resetPending; // the app closed with unread bytes while our tail was in flight: reset once acked
    private long _orphanProgressTicks; // set per idle tick once the app closed: when the sequence numbers last moved
    private uint _progressUna; // the sequence numbers at the last orphan sample
    private uint _progressRcv;
    private bool _establishedCounted; // DIAGNOSTIC: whether we incremented LocalTcpStack.EstablishedConnections
    private bool _sndNxtAfterSynSet;
    private bool _disposed;
    private bool _closedFlag;
    private long _lastActivityTicks = Stopwatch.GetTimestamp();
    private bool _appToNetCompleted;
    private uint _sndNxt = isnLocal; // SYN sequence; bumped to ISN+1 after SYN-ACK is sent.
    private uint _sndUna = isnLocal; // Oldest unacknowledged byte.
    private readonly byte _peerWsShift = peerWsShift > 14 ? (byte)14 : peerWsShift; // Peer's window scale shift (RFC 1323).
    private uint _peerWindow = 0xFFFF; // Peer's last advertised receive window (scaled). Initial guess until the first ACK.
    private uint _rcvNxt = isnRemote + 1; // We have already "consumed" the peer's SYN.
    private int _unackedSegments; // Count of in-order full-size data segments not yet acknowledged.
    private long _delayedAckSinceTicks; // When _unackedSegments went 0 -> 1 (drives the delayed-ACK flush).
    private int _ackCount; // trace counters, counted under VerboseLogging only
    private int _retxCount;

    // Loss recovery: a coarse RTO (RFC 6298-inspired) serviced by the stack's maintenance sweep.
    // Fast retransmit needs 3 dup-ACKs, which require LATER segments arriving after the hole — the last
    // segment of a burst (tail loss), a FIN, or a SYN-ACK have none, so without this timer a single
    // dropped packet stalled the flow until the idle timeout (15 min default).
    private readonly int _rtoInitialMs = (int)options.RetransmitTimeout.TotalMilliseconds;
    private readonly int _rtoMaxMs = (int)options.RetransmitMaxTimeout.TotalMilliseconds;
    private readonly int _delayedAckMs = (int)options.DelayedAckTimeout.TotalMilliseconds;
    private long _rtoStartTicks = Stopwatch.GetTimestamp();
    private int _rtoCurrentMs = (int)options.RetransmitTimeout.TotalMilliseconds;
    private bool _maintenanceInterestHeld; // registered with the stack's maintenance sweep (guarded by _seqLock)

    // Kept for fast retransmit (RFC 5681) and the RTO: on loopback the TUN or the kernel drops packets under load, with
    // the effect of loss. It bounds the bytes in flight.
    private readonly RetransmitBuffer _retransmitBuffer = new(options.RetransmitBufferSize);
    private uint _lastDupAck;
    private int _dupAckCount;
    private bool _inRecovery; // a retransmission started loss recovery, which ends when SndUna reaches _recoverySeq
    private uint _recoverySeq; // SndNxt when the loss was detected (RFC 6582's recovery point)
    public IPEndPointPairValue IpEndPointPair => ipEndPointPair;
    public uint IsnLocal { get; } = isnLocal;
    public ushort Mss { get; } = ClampMss(peerMss, options);
    public TcpConnectionState State { get; private set; } = TcpConnectionState.SynReceived;

    /// <summary>The unread bytes in the pipe, for the stack-wide diagnostic total.</summary>
    internal int PipeUnread => _receiveWindow.Unread;

    /// <summary>Single-lock snapshot of everything needed to emit a pure ACK or SYN-ACK.</summary>
    internal (uint SndNxt, uint RcvNxt, ushort Window) SnapshotForAck()
    {
        lock (_seqLock)
            return (_sndNxt, _rcvNxt, _receiveWindow.Advertise());
    }

    /// <summary>What the stream reads: the bytes from the peer.</summary>
    public PipeReader NetToAppReader => _receiveWindow.Reader;

    /// <summary>
    /// Event raised when connection is fully closed and should be removed from the stack.
    /// </summary>
    public event Action<LocalTcpConnection>? OnClosed;

    private static ushort ClampMss(ushort? peerMss, LocalTcpStackOptions options)
    {
        if (peerMss is null or 0) return options.DefaultMss;
        var v = peerMss.Value;
        if (v < options.MinMss) return options.MinMss;  // pathological lower bound
        if (v > options.MaxMss) return options.MaxMss;
        return v;
    }

    /// <summary>
    /// Wires the stack reference and registers with the stack's maintenance sweep.
    /// Called BEFORE the first SYN-ACK is sent so maintenance (SYN-ACK retransmission) is armed
    /// from the very first packet.
    /// </summary>
    public void Start(LocalTcpStack stack)
    {
        _stack = stack;

        // Pre-create the client (and its stream) that will be enqueued once handshake completes.
        var stream = new LocalTcpStream(this, stack);
        _pendingClient = new LocalTcpClient(
            stream,
            ipEndPointPair.Destination.ToIPEndPoint(),
            ipEndPointPair.Source.ToIPEndPoint());

        // Register with the stack's maintenance sweep: a SynReceived connection needs SYN-ACK
        // retransmission service until the handshake completes.
        lock (_seqLock)
            UpdateMaintenanceInterestLocked();
    }

    /// <summary>
    /// The app closed its stream, so both directions end. Bytes the app never read, or new bytes the peer
    /// sends afterwards, reset the connection as a kernel does after close() (RFC 1122 4.2.2.13) - once our
    /// own tail data is acknowledged (see <see cref="TryHandleIncoming"/>), so the peer does not drop that
    /// tail unread; otherwise a FIN goes out and the peer's ACK of it ends the connection.
    /// </summary>
    public void CloseFromApp(LocalTcpStack stack)
    {
        var reset = false;
        lock (_seqLock) {
            if (_disposed || _closedFlag) return;
            _readSideClosed = true;
            _appToNetCompleted = true;
            // With our tail data still in flight the RST would make the peer drop it unread (RFC 7230 6.6):
            // then the FIN goes first and the RST follows the ACK of the tail (TryHandleIncoming). The unread
            // bytes leave the pipe total now either way; nobody will read them.
            var discarded = _receiveWindow.ClearUnread();
            reset = discarded > 0 && _sndUna == _sndNxt;
            _resetPending = discarded > 0 && !reset;
        }

        if (reset) {
            Abort();
            return;
        }

        CompleteNetToApp();
        TryStartFin(stack);
    }

    /// <summary>
    /// Sets SndNxt to ISN + 1 the first time a SYN-ACK is sent. Idempotent for SYN retransmits.
    /// </summary>
    public void SetSndNxtAfterSyn()
    {
        lock (_seqLock) {
            if (_sndNxtAfterSynSet) return;
            _sndNxt = IsnLocal + 1;
            _sndUna = IsnLocal + 1;
            _sndNxtAfterSynSet = true;
        }
    }

    /// <summary>
    /// Transitions from SynReceived to Established and hands the pending stream to the listener.
    /// Idempotent: subsequent calls are no-ops.
    /// </summary>
    public void MarkEstablished()
    {
        LocalTcpClient? clientToEnqueue;
        lock (_seqLock) {
            // after a dispose nobody is left to count it down; after a close its client must not reach the accept queue
            if (_disposed || _closedFlag || State != TcpConnectionState.SynReceived) return;
            State = TcpConnectionState.Established;
            _establishedCounted = true;
            clientToEnqueue = _pendingClient;
            _pendingClient = null;
            UpdateMaintenanceInterestLocked(); // handshake done -> SYN-ACK retransmission no longer needed
        }

        // Off the hot _seqLock: the State==SynReceived guard above guarantees exactly one thread reaches
        // here, so the count+log can run outside the lock.
        diagnostics.IncrementEstablishedConnections(ipEndPointPair); // DIAGNOSTIC (counts + logs)

        if (clientToEnqueue != null && !listener.TryEnqueueAccept(clientToEnqueue)) {
            // Listener stopped or its accept queue is full: abort so the peer gets an RST immediately
            // instead of a fully-established connection that lingers half-closed buffering data it can
            // never deliver, then dispose the unaccepted client (its FIN attempt becomes a no-op).
            Abort();
            clientToEnqueue.Dispose();
        }
    }

    /// <summary>
    /// The ACK that completes the handshake acknowledges our SYN-ACK; its sequence may sit anywhere up to the
    /// window's edge (RFC 9293 3.10.7.4; the edge too, where Linux takes a challenge ACK with the whole window in
    /// flight): after a lost third ACK the peer's first data may be lost too, and the data path dup-acks the gap.
    /// </summary>
    public bool IsValidHandshakeAck(uint seq, uint ack, TcpFlags flags)
    {
        if (!flags.HasFlag(TcpFlags.Ack))
            return false;

        lock (_seqLock) {
            var seqDiff = (int)(seq - _rcvNxt);
            return State == TcpConnectionState.SynReceived &&
                   ack == _sndNxt &&
                   seqDiff >= 0 && seqDiff <= _receiveWindow.LastAdvertised;
        }
    }

    /// <summary>Whether the ack acknowledges our SYN-ACK: the peer has seen it and is established on its side.</summary>
    internal bool AcknowledgesOurSyn(uint ack)
    {
        lock (_seqLock)
            return ack == _sndNxt;
    }

    public void Dispose()
    {
        if (_disposed) return;

        LocalTcpClient? abandoned;
        bool decrementEstablished;
        lock (_seqLock) {
            if (_disposed) return;
            _disposed = true;
            _finSent = true; // suppress a stray FIN from a later graceful-close after a direct dispose
            _receiveWindow.ClearUnread();
            _receiveWindow.Complete(); // under the lock, like CompleteNetToApp: a parked ReadAsync returns EOF, and no
                                       // later write lands in a pipe whose count was just cleared
            abandoned = _pendingClient;
            _pendingClient = null;
            decrementEstablished = _establishedCounted;
            _establishedCounted = false;
            UpdateMaintenanceInterestLocked();
        }

        VhTypeTracker.Record("LocalTcpConnection.disposed"); // every teardown ends here, a close's as well as a direct dispose's
        if (decrementEstablished)
            diagnostics.DecrementEstablishedConnections(ipEndPointPair, "dispose"); // DIAGNOSTIC (counts + logs)

        // A direct Dispose from the stack (its Dispose, or a connection it could not add or start) does not go through
        // Close, so it unblocks the app side itself: the pipe completed above, the not-yet-accepted client disposed, and a
        // SendAppDataAsync waiting on the window signal released. Without this the proxy copy loop hangs and the connection
        // and its pipe leak.
        abandoned?.Dispose();
        _appToNetCompleted = true;
        TrySignalWindow();
    }

    /// <summary>
    /// Writes data from app directly as TCP segments to the network (inline on caller's thread).
    /// Respects the peer's advertised receive window. Throws <see cref="IOException"/> when the
    /// connection is (or becomes) closed for writing, and <see cref="OperationCanceledException"/>
    /// on caller cancellation — a partial write is never silently reported as success.
    /// </summary>
    public async ValueTask SendAppDataAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        ThrowIfWriteClosed();
        Touch(Stopwatch.GetTimestamp());

        var stack = _stack;
        if (stack == null) return;

        var mss = Mss;
        var offset = 0;

        while (offset < data.Length) {
            ct.ThrowIfCancellationRequested();
            ThrowIfWriteClosed();

            // Determine how much we can send within the peer's window.
            int allowable;
            {
                // Drain any stale signal first, then snapshot the window.
                // ACKs arriving during a burst leave a signal in the semaphore that
                // doesn't correspond to new window space.  Draining before checking
                // ensures we don't consume a signal and then find allowable==0 again.
                DrainWindowSignal();

                uint pw, una, nxt;
                int retxFree;
                lock (_seqLock) {
                    // Close and Dispose set these under this lock before they signal the window, so a writer
                    // about to wait either sees them here or finds their signal after its drain - never both
                    // missed, which would park it for good on a full buffer.
                    if (_disposed || _appToNetCompleted || _finSent)
                        throw new IOException("The TCP connection was closed while writing.");
                    pw = _peerWindow; una = _sndUna; nxt = _sndNxt;
                    var inFlight = (long)(nxt - una);
                    var fromPeer = (int)Math.Max(0, _peerWindow - inFlight);
                    retxFree = _retransmitBuffer.Free;
                    allowable = Math.Min(fromPeer, retxFree);
                }

                if (allowable <= 0) {
                    diagnostics.TraceZeroWindowWait(ipEndPointPair, offset, data.Length, pw, una, nxt);
                    if (retxFree <= 0) {
                        // The retransmit buffer is the limit: a full window is in flight, and its loss is the
                        // RTO's business (PollMaintenance). Wait for the ACK that frees buffer space, or the
                        // close; TrySignalWindow wakes this on both. Caller cancellation propagates as
                        // OperationCanceledException - a partial write is never reported as success.
                        await _windowSignal.WaitAsync(ct).Vhc();
                        continue;
                    }
                    // The peer's window is the limit: after the interval, a zero-window probe carrying one
                    // new byte (RFC 9293 3.8.6.1) draws the window update some peers (Android) send only to
                    // a probe.
                    var signaled = await _windowSignal.WaitAsync(options.ZeroWindowProbeInterval, ct).Vhc();
                    if (!signaled && offset < data.Length) {
                        diagnostics.TraceZeroWindowProbe(ipEndPointPair, offset, pw);
                        offset += SendZeroWindowProbe(stack, data.Span[offset]);
                    }
                    continue;
                }
            }

            var remaining = data.Length - offset;
            var burst = Math.Min(remaining, allowable);

            while (burst > 0) {
                var segLen = Math.Min(burst, mss);
                var segmentData = data.Span.Slice(offset, segLen);

                uint seqForSegment;
                uint ackForSegment;
                ushort windowForSegment;
                lock (_seqLock) {
                    // Recheck under the lock: StartFin takes its FIN sequence number under this same
                    // lock, so this guard guarantees no data segment is ever emitted at a sequence
                    // number beyond an already-sent FIN (which the peer would discard = data loss).
                    if (_disposed || _appToNetCompleted || _finSent)
                        throw new IOException("The TCP connection was closed while writing.");

                    _retransmitBuffer.Append(segmentData); // first: a refused segment changes nothing else
                    seqForSegment = _sndNxt;
                    ackForSegment = _rcvNxt;
                    // RTO: (re)arm when this segment opens a new in-flight window.
                    if (_sndUna == _sndNxt)
                        RestartRtoLocked(Stopwatch.GetTimestamp());
                    _sndNxt += (uint)segLen;
                    windowForSegment = _receiveWindow.Advertise();
                    HoldMaintenanceInterestLocked();
                }

                var flags = offset + segLen >= data.Length ? TcpFlags.Ack | TcpFlags.Psh : TcpFlags.Ack; // PSH ends the write
                stack.SendPacket(BuildSegment(seqForSegment, ackForSegment, windowForSegment, flags, segmentData));
                offset += segLen;
                burst -= segLen;
            }
        }
    }

    private void ThrowIfWriteClosed()
    {
        if (_disposed || _appToNetCompleted)
            throw new IOException("The TCP connection has been closed.");
    }

    /// <summary>
    /// Processes a segment from the peer that passed the stack's handshake check (a RST skips it): its RST, else its ACK,
    /// then its data and FIN. Returns whether the segment was processed and whether it needs an ACK.
    /// </summary>
    public (bool handled, bool needsAck) TryHandleIncoming(uint seq, uint ack, ushort windowSize, TcpFlags flags, ReadOnlySpan<byte> payload)
    {
        if (_disposed) return (false, false);
        var now = Stopwatch.GetTimestamp();

        if (flags.HasFlag(TcpFlags.Rst))
            return HandleRst(seq);

        // After the handshake every segment carries an ACK (RFC 9293 3.10.7.4): one without is dropped with its data.
        if (!flags.HasFlag(TcpFlags.Ack))
            return (false, false);

        return HandleAck(seq, ack, windowSize, flags, payload.Length, now) ?? HandleData(seq, flags, payload, now);
    }

    // RFC 5961 3.2: a RST is taken only at exactly RCV.NXT; elsewhere inside the window it draws a challenge ACK, outside
    // it is dropped - so a local app able to inject one packet into the TUN cannot blind-reset a flow.
    private (bool handled, bool needsAck) HandleRst(uint seq)
    {
        bool exact, inWindow;
        lock (_seqLock) {
            var rstDiff = (int)(seq - _rcvNxt);
            exact = rstDiff == 0;
            inWindow = rstDiff > 0 && rstDiff < Math.Max(1, (int)_receiveWindow.LastAdvertised);
        }

        if (exact) {
            Close();
            return (false, false);
        }
        return inWindow ? (true, true) : (false, false);
    }

    /// <summary>
    /// The ACK every segment carries: an acceptable one advances SndUna and takes the peer's window, a pure duplicate is
    /// counted, and the retransmissions they call for go out. Returns the segment's result when the ACK alone settles it
    /// (a challenge ACK, the reset CloseFromApp deferred, the end of the FIN exchange), null when its data and FIN are
    /// still to be processed.
    /// </summary>
    private (bool handled, bool needsAck)? HandleAck(uint seq, uint ack, ushort windowSize, TcpFlags flags, int payloadLength, long now)
    {
        var fin = flags.HasFlag(TcpFlags.Fin);
        bool acceptable, challenge;
        var retransmit = false;
        IpPacket? finRetransmit = null;
        var finExchangeComplete = false;
        var resetUnread = false;
        uint prevUna, sndNxt, peerWindow;
        lock (_seqLock) {
            prevUna = _sndUna;
            sndNxt = _sndNxt;
            var scaledWindow = (uint)windowSize << _peerWsShift;
            // RFC 9293 3.10.7.4: only a segment inside the receive window with an ack within [SndUna, SndNxt] is
            // acceptable - processed, carrying the peer's window, counting as its activity (a keepalive, one below RcvNxt,
            // too); an ack of data never sent draws a challenge ACK and nothing else (RFC 5961 5.2).
            var acked = ack - _sndUna;
            var ackCurrent = acked <= _sndNxt - _sndUna;
            challenge = !ackCurrent && (int)(ack - _sndNxt) > 0;
            acceptable = ackCurrent && SegmentInWindowLocked(seq, payloadLength, fin);

            if (acceptable && acked > 0) {
                _sndUna = ack;
                _retransmitBuffer.Trim((int)acked);
                _dupAckCount = 0;
                _lastDupAck = ack;
                if (_sndUna != _sndNxt)
                    RestartRtoLocked(now); // progress restarts the RTO; an emptied flight needs none, the next send arms it
                // Loss recovery (RFC 6582): a partial ACK, short of the recovery point, names the next lost segment,
                // retransmitted now instead of after another RTO - the FIN when the data is all acked and it is the lost one.
                if (_inRecovery) {
                    if ((int)(_sndUna - _recoverySeq) >= 0)
                        _inRecovery = false;
                    else if (_retransmitBuffer.Length > 0)
                        retransmit = true;
                    else if (_finSent && _sndNxt != _sndUna)
                        finRetransmit = BuildFinPacket(_sndNxt - 1, _rcvNxt, _receiveWindow.Advertise());
                }
                // LastAck: the peer acknowledged our FIN after its own; nothing is left to exchange.
                finExchangeComplete = _finSent && _finReceived && _sndUna == _sndNxt;
                // The reset CloseFromApp deferred (unread bytes, our tail in flight): everything is acked now.
                resetUnread = _resetPending && _sndUna == _sndNxt;
            }

            // A pure duplicate ACK (RFC 5681: no data, no FIN, the window unchanged and open) says the peer misses the
            // data at SndUna. A window update must not count, or three of them would trigger a spurious fast retransmit;
            // nor may a zero window, whose ACKs of our probes are flow control, not loss - a recovery they started would
            // outlive the window and swallow the real duplicate ACKs after it reopens.
            var duplicateAck = acceptable && acked == 0 && payloadLength == 0 && !fin && _retransmitBuffer.Length > 0 &&
                               scaledWindow == _peerWindow && scaledWindow != 0;
            if (duplicateAck) {
                if (ack == _lastDupAck) _dupAckCount++;
                else { _lastDupAck = ack; _dupAckCount = 1; }
                // Three duplicate ACKs start one recovery; more of them during it retransmit nothing - the partial ACKs
                // above carry the recovery segment by segment.
                if (_dupAckCount >= 3 && !_inRecovery) {
                    retransmit = true;
                    EnterRecoveryLocked();
                }
            }

            if (acceptable)
                _peerWindow = scaledWindow;
            peerWindow = _peerWindow;
        }

        if (challenge)
            return (true, true);
        if (acceptable)
            Touch(now);
        if (diagnostics.VerboseLogging)
            diagnostics.TraceAck(ipEndPointPair, ++_ackCount, ack, prevUna, sndNxt, ack - prevUna, windowSize, peerWindow,
                payloadLength, _windowSignal.CurrentCount, _dupAckCount);

        if (retransmit)
            FastRetransmit();
        if (finRetransmit != null)
            TrySendPacket(finRetransmit);
        if (acceptable)
            TrySignalWindow();

        if (resetUnread) {
            Abort();
            return (false, false);
        }
        if (finExchangeComplete) {
            Close();
            // A retransmitted peer FIN on this segment still needs its ACK, or its next retransmit meets an
            // unknown-connection RST.
            return (true, fin);
        }
        return null;
    }

    // _seqLock must be held. RFC 9293 3.10.7.4's test of a segment's sequence numbers against the receive window: it
    // starts inside the window (the edge included), or it started before RcvNxt and reaches past it (a retransmission
    // carrying new bytes or the FIN), or it is a keepalive: at most one byte at RcvNxt - 1.
    // AggressiveInlining, here and on the receive path's two other helpers: they run per segment and were inline before
    // they had names; on its own Mono (Android) inlines nothing this size, and RyuJIT (iOS, desktop) only when its
    // heuristic agrees.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool SegmentInWindowLocked(uint seq, int payloadLength, bool fin)
    {
        var seqDiff = (int)(seq - _rcvNxt);
        if (seqDiff >= 0)
            return seqDiff <= _receiveWindow.LastAdvertised;
        return seqDiff + payloadLength + (fin ? 1 : 0) > 0 || seqDiff == -1 && payloadLength <= 1;
    }

    /// <summary>The segment's data and FIN, after its ACK.</summary>
    private (bool handled, bool needsAck) HandleData(uint seq, TcpFlags flags, ReadOnlySpan<byte> payload, long now)
    {
        try {
            bool needsAck, dataAfterClose, resetAfterClose, finTaken;
            var finCloses = false;
            lock (_seqLock) {
                var seqDiff = (int)(seq - _rcvNxt);
                // Ahead of RcvNxt: a duplicate ACK names the gap and no reassembly holds the segment (a start exactly half
                // the sequence space away counts as ahead: its negation would overflow the overlap below).
                if (seqDiff > 0 || seqDiff == int.MinValue)
                    return (true, true);

                // At or before RcvNxt: in order (seqDiff == 0) or a retransmission overlapping what was received
                // (seqDiff < 0); the bytes past RcvNxt are the new ones.
                var isRetransmit = seqDiff < 0;
                var overlap = -seqDiff;
                var newBytes = payload.Length - overlap;

                // New bytes after the app closed its stream have no reader: rather than fill a dead pipe that pins the
                // connection, they reset it, as a kernel does after close(), once our own tail is acknowledged; until then
                // they are dropped without an ACK (a RST now would make the peer drop that tail unread) and their
                // retransmission draws the RST.
                dataAfterClose = newBytes > 0 && _readSideClosed;
                resetAfterClose = dataAfterClose && _sndUna == _sndNxt;
                // The window is enforced: bytes beyond it are dropped, as a kernel drops them, so a peer that ignores the
                // window retransmits into a bounded pipe, which also stays below its pause threshold
                // (LocalTcpStackOptions.CreatePipeOptions).
                var accepted = newBytes > 0 && !dataAfterClose ? Math.Min(newBytes, _receiveWindow.Available) : 0;
                if (accepted > 0) {
                    _receiveWindow.Write(payload.Slice(overlap, accepted));
                    _rcvNxt += (uint)accepted;
                }
                var truncated = accepted < newBytes && !dataAfterClose;

                // Only a segment with something to acknowledge reaches AckNowLocked: its reset of the count would cancel a
                // pending delayed ACK with no ACK sent.
                var delayable = !isRetransmit && !truncated && payload.Length >= Mss &&
                                !flags.HasFlag(TcpFlags.Fin) && !flags.HasFlag(TcpFlags.Psh);
                needsAck = (isRetransmit || accepted > 0 || truncated) && AckNowLocked(delayable, now);
                finTaken = TakeFinLocked(flags, seq + (uint)payload.Length);
                if (finTaken) {
                    needsAck = true;
                    finCloses = State == TcpConnectionState.Closed;
                }
                // The after-close exit is below the ACK bookkeeping, which a retransmission after the close goes through too.
            }

            if (dataAfterClose) {
                if (resetAfterClose)
                    Abort();
                return (false, false);
            }
            if (finTaken) {
                CompleteNetToApp();
                if (finCloses)
                    Close();
            }
            return (true, needsAck);
        }
        catch (InvalidOperationException ex) {
            // a pipe or the window that throws: logged, and the peer gets a RST instead of a black hole
            diagnostics.LogFailure(nameof(HandleData), ex);
            Abort();
            return (false, false);
        }
    }

    // _seqLock must be held and the segment has something to acknowledge. The delayed-ACK policy, allocation-free: a
    // delayable segment (clean, in order, full size) is acknowledged every second one, the odd one by the maintenance
    // sweep within DelayedAckTimeout, so the iOS packet path is not flooded; everything else at once.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AckNowLocked(bool delayable, long now)
    {
        if (delayable && ++_unackedSegments < 2) {
            _delayedAckSinceTicks = now;
            HoldMaintenanceInterestLocked(); // the sweep flushes this ACK if no second segment comes
            return false;
        }
        _unackedSegments = 0;
        return true;
    }

    // _seqLock must be held. Takes the peer's FIN once every byte before it is consumed; its own position (seq + payload
    // length) is checked, not seqDiff == 0, so a FIN inside a partially overlapping retransmission is taken too.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TakeFinLocked(TcpFlags flags, uint finSeq)
    {
        if (!flags.HasFlag(TcpFlags.Fin) || _finReceived || finSeq != _rcvNxt)
            return false;
        _rcvNxt += 1;
        _finReceived = true;
        // Both FINs exchanged: closed once ours is acknowledged too (this segment's ACK was processed first); otherwise
        // LastAck retransmits it until the ACK arrives.
        if (_finSent)
            State = _sndUna == _sndNxt ? TcpConnectionState.Closed : TcpConnectionState.LastAck;
        else
            State = TcpConnectionState.Closing;
        return true;
    }

    /// <summary>The stream read count bytes from the pipe. Flow control, not a diagnostic: the drain is what reopens the
    /// advertised window, and this is the one sender of the window-update ACK that lets a throttled peer resume at once
    /// instead of waiting for its zero-window-probe timer.</summary>
    public void OnAppConsumed(long count)
    {
        if (count <= 0) return;

        bool sendUpdate;
        lock (_seqLock) {
            if (_disposed) return;
            // The decision and the flag it clears are under _seqLock, so a write that just re-closed the window is never
            // overwritten by a stale snapshot.
            sendUpdate = _receiveWindow.Consume(count);
        }

        if (sendUpdate)
            try { _stack?.SendAckOnly(this); } catch { /* best-effort window update */ }
    }

    private void Touch(long now)
    {
        Volatile.Write(ref _lastActivityTicks, now); // atomic on 32-bit too, where a plain long store could tear
    }

    /// <summary>Time since the last send/receive activity on this connection (drives LRU eviction). For an
    /// orphan (the app closed its stream) it is the time since the sequence numbers last moved, sampled by
    /// the idle sweep (<see cref="PollIdle"/>), so a peer's no-progress ACKs cannot keep it from eviction.</summary>
    internal TimeSpan IdleDuration {
        get {
            var progress = Volatile.Read(ref _orphanProgressTicks);
            return Stopwatch.GetElapsedTime(progress != 0 ? progress : Volatile.Read(ref _lastActivityTicks));
        }
    }

    /// <summary>True once this connection has been closed/disposed (skip it when picking an eviction victim).</summary>
    internal bool IsClosed => Volatile.Read(ref _closedFlag) || _disposed;

    /// <summary>True once the app→net direction can no longer accept writes (FIN sent, closed, or
    /// disposed). Surfaces connection liveness to <see cref="LocalTcpStream.CanWrite"/> so consumers'
    /// health checks (e.g. ProxyChannel.CheckAlive) see a dead write path.</summary>
    internal bool IsWriteClosed => _appToNetCompleted || IsClosed;

    /// <summary>Lock-free hint for the stack's maintenance sweep (dirty read is fine; the next sweep
    /// tick observes the settled value).</summary>
    internal bool HasMaintenanceInterest => _maintenanceInterestHeld;

    // _seqLock must be held. Registers/unregisters this connection with the stack's maintenance sweep
    // (SYN-ACK retransmission, RTO, delayed-ACK flush). Interest is held while there is something the
    // sweep could act on, and given back by the sweep's tick once there is nothing, so a fully idle
    // stack keeps its maintenance timer parked (no periodic wake-ups — battery matters on mobile).
    private void UpdateMaintenanceInterestLocked()
    {
        var stack = _stack;
        if (stack == null) return; // Start() registers the initial interest once wired

        var desired = MaintenanceWantedLocked;
        if (desired == _maintenanceInterestHeld) return;
        _maintenanceInterestHeld = desired;
        if (desired) stack.AddMaintenanceInterest();
        else stack.RemoveMaintenanceInterest();
    }

    // _seqLock must be held. The send and receive paths only take interest; the sweep gives it back at a tick with
    // nothing to do (PollMaintenance). Giving it back per packet flipped the stack's timer on every other segment
    // of an upload (the delayed-ACK count goes 1, 0, 1, 0), a Timer.Change each time.
    private void HoldMaintenanceInterestLocked()
    {
        if (_maintenanceInterestHeld || !MaintenanceWantedLocked) return;
        var stack = _stack;
        if (stack == null) return;
        _maintenanceInterestHeld = true;
        stack.AddMaintenanceInterest();
    }

    private bool MaintenanceWantedLocked => !_disposed && !_closedFlag &&
        (State == TcpConnectionState.SynReceived || _sndNxt != _sndUna || _unackedSegments > 0);

    // _seqLock must be held.
    private void RestartRtoLocked(long now)
    {
        _rtoStartTicks = now;
        _rtoCurrentMs = _rtoInitialMs;
    }

    /// <summary>
    /// Called by the stack's maintenance sweep (only while this connection holds maintenance interest).
    /// Provides the timer-driven behaviors a minimal TCP needs beyond fast retransmit:
    /// (a) SYN-ACK retransmission while the handshake is incomplete — a lost final ACK otherwise
    ///     strands server-speaks-first connections in SynReceived until the idle timeout; given up,
    ///     with a RST, once the backoff has reached its cap and the peer is still silent;
    /// (b) a coarse RTO for unacked data / FIN — the ONLY recovery for tail loss, where no later
    ///     segment ever arrives to generate the 3 duplicate ACKs fast retransmit needs;
    /// (c) flushing a thinned (delayed) ACK so an odd trailing full-size segment is acknowledged
    ///     within DelayedAckTimeout instead of waiting for the peer's RTO (RFC 1122's 500 ms bound).
    /// </summary>
    internal void PollMaintenance()
    {
        if (_disposed || IsClosed) return;
        var stack = _stack;
        if (stack == null) return;

        var resendSynAck = false;
        var abortHandshake = false;
        var retxData = false;
        var flushDelayedAck = false;
        IpPacket? finRetx = null;

        lock (_seqLock) {
            if (_disposed || _closedFlag) return;

            // (c) delayed-ACK flush
            if (_unackedSegments > 0 &&
                Stopwatch.GetElapsedTime(_delayedAckSinceTicks).TotalMilliseconds >= _delayedAckMs) {
                _unackedSegments = 0;
                flushDelayedAck = true;
            }

            // (a)/(b) retransmission on ACK silence
            var rtoElapsedMs = Stopwatch.GetElapsedTime(_rtoStartTicks).TotalMilliseconds;
            if (rtoElapsedMs >= _rtoCurrentMs) {
                if (State == TcpConnectionState.SynReceived) {
                    // A kernel answers a retransmitted SYN-ACK at once, with its ACK or a RST, so a peer still silent
                    // when the backoff has reached its cap is gone or hostile: the handshake is given up, which bounds
                    // what a SYN with no ACK can hold (a SYN flood from a local raw socket, a stuck handshake).
                    if (_rtoCurrentMs >= _rtoMaxMs)
                        abortHandshake = true;
                    else {
                        resendSynAck = true;
                        BackoffRtoLocked();
                    }
                }
                else if (_sndNxt != _sndUna) {
                    if (_retransmitBuffer.Length > 0) {
                        retxData = true;
                        // The ACKs of this retransmission carry the rest segment by segment - unless the peer's
                        // window is zero: that is flow control, not loss, and a recovery the zero window
                        // interrupted ends here so the duplicate ACKs after it reopens count again.
                        if (_peerWindow != 0) EnterRecoveryLocked();
                        else _inRecovery = false;
                    }
                    else if (_finSent)
                        // All data is acked; the single outstanding sequence number is the FIN.
                        finRetx = BuildFinPacket(_sndNxt - 1, _rcvNxt, _receiveWindow.Advertise());
                    BackoffRtoLocked();
                }
            }

            UpdateMaintenanceInterestLocked();
        }

        if (abortHandshake) {
            Abort();
            return;
        }

        if (flushDelayedAck)
            try { stack.SendAckOnly(this); } catch { /* next sweep retries */ }
        if (resendSynAck)
            try { stack.SendSynAck(this); } catch { /* next sweep retries */ }
        if (retxData)
            FastRetransmit();
        if (finRetx != null)
            TrySendPacket(finRetx);
    }

    // _seqLock must be held. Loss detected (three duplicate ACKs or the RTO): until SndUna passes today's
    // SndNxt, every partial ACK retransmits the next lost segment and duplicate ACKs trigger nothing more.
    private void EnterRecoveryLocked()
    {
        _inRecovery = true;
        _recoverySeq = _sndNxt;
    }

    // _seqLock must be held.
    private void BackoffRtoLocked()
    {
        _rtoStartTicks = Stopwatch.GetTimestamp();
        _rtoCurrentMs = Math.Min(_rtoCurrentMs * 2, _rtoMaxMs);
    }

    /// <summary>
    /// Called by the stack's idle sweep every IdleCheckInterval. An orphan (the app closed its stream) is idle when the
    /// sequence numbers stop moving: the peer's window-0 ACKs of our retransmits or its keepalives would otherwise keep
    /// it "active" and pin its slot forever; sampled per tick, never per packet, and IdleDuration (the eviction's
    /// measure too) reads the sample. An idle connection is aborted: the peer gets a RST, not a black hole.
    /// </summary>
    internal void PollIdle()
    {
        if (_disposed || IsClosed) return;

        if (_readSideClosed) {
            var (una, rcv) = SnapshotSequenceNumbers();
            if (_orphanProgressTicks == 0 || una != _progressUna || rcv != _progressRcv) {
                _progressUna = una;
                _progressRcv = rcv;
                Volatile.Write(ref _orphanProgressTicks, Stopwatch.GetTimestamp());
            }
        }

        if (IdleDuration >= _idleTimeout)
            Abort();
    }

    private (uint Una, uint Rcv) SnapshotSequenceNumbers()
    {
        lock (_seqLock)
            return (_sndUna, _rcvNxt);
    }

    public void StartFin(LocalTcpStack stack)
    {
        uint finSeq;
        uint finAck;
        ushort finWindow;

        lock (_seqLock) {
            if (_finSent) return;
            _finSent = true;
            _appToNetCompleted = true;

            finSeq = _sndNxt;
            finAck = _rcvNxt;
            if (_sndUna == _sndNxt)
                RestartRtoLocked(Stopwatch.GetTimestamp()); // the FIN opens a new in-flight window; arm its retransmission
            _sndNxt += 1; // FIN consumes one sequence number
            finWindow = _receiveWindow.Advertise();

            // The connection ends only when the peer acknowledges this FIN (HandleAck);
            // until then PollMaintenance retransmits it, and the idle timeout bounds a peer that never does.
            State = _finReceived ? TcpConnectionState.LastAck : TcpConnectionState.FinWait1;
            UpdateMaintenanceInterestLocked();
        }

        // Built outside _seqLock: PacketBuilder rents pooled memory and needs no sequence state.
        var finPacket = BuildFinPacket(finSeq, finAck, finWindow);
        stack.SendPacket(finPacket);
    }

    private IpPacket BuildFinPacket(uint seq, uint ackNum, ushort window) =>
        BuildSegment(seq, ackNum, window, TcpFlags.Ack | TcpFlags.Fin, ReadOnlySpan<byte>.Empty);

    // PacketBuilder rents the packet's memory; the send owns the packet from there. Inlined: it builds every data segment.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IpPacket BuildSegment(uint seq, uint ack, ushort window, TcpFlags flags, ReadOnlySpan<byte> payload)
    {
        var packet = PacketBuilder.BuildTcp(IpEndPointPair.Destination, IpEndPointPair.Source,
            options: ReadOnlySpan<byte>.Empty, payload: payload);
        var tcp = packet.ExtractTcp();
        tcp.SequenceNumber = seq;
        tcp.AcknowledgmentNumber = ack;
        tcp.WindowSize = window;
        tcp.Flags = (byte)flags; // TcpFlags are the wire bits
        return packet;
    }

    public void TryStartFin(LocalTcpStack stack)
    {
        try { StartFin(stack); } catch { /* ignore */ }
    }

    private void CompleteNetToApp()
    {
        // Under _seqLock, like the receive window's Write: a Close from the sweep or the drop path must not complete the
        // writer while the receive thread is between GetSpan and Advance, which System.IO.Pipelines forbids. The pipe uses
        // the ThreadPool scheduler, so Complete never resumes the reader inline.
        lock (_seqLock)
            _receiveWindow.Complete();
    }

    private void TrySignalWindow()
    {
        if (_windowSignal.CurrentCount == 0) {
            try { _windowSignal.Release(); }
            catch (SemaphoreFullException) { /* already signalled */ }
        }
    }

    /// <summary>
    /// Zero-window probe: one NEW byte at SndNxt, stored in the retransmit buffer like any data. A probe that does not
    /// advance the sequence number draws no window update over QUIC's tight per-stream window (see
    /// ZeroWindowProbe_ShouldMakeForwardProgress). Returns the bytes consumed: 1, or 0 when nothing could
    /// be sent (the connection is closing, or the buffer has no room - the caller probes only while it has).
    /// </summary>
    private int SendZeroWindowProbe(LocalTcpStack stack, byte probeByte)
    {
        if (_disposed || _appToNetCompleted) return 0;

        uint probeSeq;
        uint probeAck;
        ushort probeWindow;
        lock (_seqLock) {
            if (_disposed || _appToNetCompleted || _finSent || _retransmitBuffer.Free == 0) return 0;

            _retransmitBuffer.Append([probeByte]); // first, like the data segment's
            probeSeq = _sndNxt;
            if (_sndUna == _sndNxt)
                RestartRtoLocked(Stopwatch.GetTimestamp());
            _sndNxt += 1;
            probeAck = _rcvNxt;
            probeWindow = _receiveWindow.Advertise();
            HoldMaintenanceInterestLocked();
        }

        // The sequence state is advanced and the byte is in the buffer, so a failed send is a dropped packet that the RTO
        // retransmits; 1 is returned either way, keeping the caller's offset in step with SndNxt (0 would duplicate the byte).
        try { stack.SendPacket(BuildSegment(probeSeq, probeAck, probeWindow, TcpFlags.Ack, [probeByte])); }
        catch { /* a lost packet */ }
        return 1;
    }

    private void DrainWindowSignal()
    {
        while (_windowSignal.Wait(0)) { }
    }

    private void FastRetransmit()
    {
        var stack = _stack;
        if (stack == null || _disposed) return;

        byte[] payloadCopy;
        uint seqForSegment;
        uint ackForSegment;
        ushort windowForSegment;
        int retxLen;
        lock (_seqLock) {
            if (_retransmitBuffer.Length == 0) return;
            retxLen = _retransmitBuffer.Length;
            payloadCopy = _retransmitBuffer.CopyFront(Mss);
            seqForSegment = _sndUna;
            ackForSegment = _rcvNxt;
            windowForSegment = _receiveWindow.Advertise();
        }

        if (diagnostics.VerboseLogging)
            diagnostics.TraceFastRetransmit(ipEndPointPair, Interlocked.Increment(ref _retxCount), seqForSegment, retxLen);
        var packet = BuildSegment(seqForSegment, ackForSegment, windowForSegment, TcpFlags.Ack | TcpFlags.Psh, payloadCopy);
        try { stack.SendPacket(packet); } catch { /* a lost packet: the next ACK or the RTO retransmits */ }
    }

    /// <summary>Graceful close: used when the peer already knows the connection is over (its RST, or a
    /// completed FIN exchange). Sends nothing.</summary>
    internal void Close() => Close(sendRst: false);

    /// <summary>Abortive close (idle timeout, admission eviction, broken pipe, accept-queue overflow):
    /// tears the connection down AND tells the peer with an RST, so it does not linger retransmitting
    /// into a black hole until its own timers give up.</summary>
    internal void Abort() => Close(sendRst: true);

    private void Close(bool sendRst)
    {
        // after a direct Dispose there is nothing left to close or to tell the peer
        if (_disposed || Interlocked.Exchange(ref _closedFlag, true))
            return;

        LocalTcpClient? abandoned;
        var decrementEstablished = false;
        uint rstSeq;
        uint rstAck;
        lock (_seqLock) {
            State = TcpConnectionState.Closed;
            _finSent = true;
            abandoned = _pendingClient;
            _pendingClient = null;
            // Under _seqLock: Dispose() reads/clears this flag under the same lock, so a concurrent
            // Close+Dispose can no longer both observe true and double-decrement the diagnostic.
            if (_establishedCounted) {
                _establishedCounted = false;
                decrementEstablished = true;
            }
            // At SndUna, the peer's RCV.NXT: a RST at SndNxt is outside a zero window (a rejected probe byte
            // advanced it) and silently dropped. Bytes the peer holds beyond SndUna draw its challenge ACK,
            // which the stack answers with a RST at the exact number.
            rstSeq = _sndUna;
            rstAck = _rcvNxt;
            UpdateMaintenanceInterestLocked();
        }

        if (sendRst)
            TrySendRst(rstSeq, rstAck);

        // Dispose unaccepted client if handshake never completed.
        abandoned?.Dispose();

        if (decrementEstablished)
            diagnostics.DecrementEstablishedConnections(ipEndPointPair, "close"); // DIAGNOSTIC (counts + logs)

        CompleteNetToApp();
        _appToNetCompleted = true;
        TrySignalWindow();

        try { OnClosed?.Invoke(this); } catch { /* ignore subscriber errors */ }
        Dispose();
    }

    // A failed send is a lost packet, which the RTO retransmits; SendPacket disposes the packet on its own failure.
    private void TrySendPacket(IpPacket packet)
    {
        var stack = _stack ?? throw new InvalidOperationException("The connection was not started.");
        try { stack.SendPacket(packet); } catch { /* a lost packet */ }
    }

    private void TrySendRst(uint seq, uint ackNum)
    {
        var stack = _stack;
        if (stack == null) return;
        try { stack.SendPacket(BuildSegment(seq, ackNum, window: 0, TcpFlags.Ack | TcpFlags.Rst, ReadOnlySpan<byte>.Empty)); }
        catch { /* best-effort notification */ }
    }
}
