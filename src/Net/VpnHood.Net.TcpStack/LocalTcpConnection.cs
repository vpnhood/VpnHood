using System.Diagnostics;
using System.IO.Pipelines;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.TcpStack.Primitives;
using VpnHood.Net.Toolkit.Extensions;
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
    // The values used on the data path below are cached into readonly fields once, here, so the
    // hot path (send/recv/ACK) costs exactly what the old consts did — a field read, no recompute.

    private readonly TimeSpan _idleTimeout = options.IdleTimeout;
    private readonly TimeSpan _idleCheckInterval = options.IdleCheckInterval;

    // Window-update ACK thresholds, scaled to the configured window. The old code hardcoded 16384/4096,
    // which never fired for a small ReceiveWindowSize. _windowUpdateThreshold: re-advertise once the window
    // has reopened by at least this much (1/4 window, but never below one MSS). _windowReopenFloor: also
    // re-advertise once a previously-closed window has reopened to at least this. On the default/iOS 64 KB
    // window these resolve to ~16383 and 4096, preserving the historical behavior.
    private readonly int _windowUpdateThreshold = Math.Max(ClampMss(peerMss, options), options.ReceiveWindowSize / 4);
    private readonly int _windowReopenFloor = Math.Min(4096, options.ReceiveWindowSize);

    // Pipe for network -> app data (stream reads). PipeOptions is built once per stack and shared
    // by all of its connections (this object is immutable); each connection still owns its Pipe.
    private readonly Pipe _netToAppPipe = new(pipeOptions);

    private readonly Lock _seqLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _windowSignal = new(0, 1);
    private LocalTcpClient? _pendingClient;
    private LocalTcpStack? _stack;

    private bool _finSent;
    private bool _finReceived;
    private bool _readSideClosed; // the app closed its stream: new bytes from the peer are reset, not buffered
    private bool _resetPending; // the app closed with unread bytes while our tail was in flight: reset once acked
    private long _orphanProgressTicks; // set per idle tick once the app closed: when the sequence numbers last moved
    private bool _establishedCounted; // DIAGNOSTIC: whether we incremented LocalTcpStack.EstablishedConnections
    private bool _sndNxtAfterSynSet;
    private bool _disposed;
    private bool _closedFlag;
    private long _lastActivityTicks = Stopwatch.GetTimestamp();
    private bool _netToAppCompleted;
    private bool _appToNetCompleted;
    private uint _sndNxt = isnLocal; // SYN sequence; bumped to ISN+1 after SYN-ACK is sent.
    private uint _sndUna = isnLocal; // Oldest unacknowledged byte.
    private readonly byte _peerWsShift = peerWsShift > 14 ? (byte)14 : peerWsShift; // Peer's window scale shift (RFC 1323).
    private uint _peerWindow = 0xFFFF; // Peer's last advertised receive window (scaled). Initial guess until the first ACK.
    private uint _rcvNxt = isnRemote + 1; // We have already "consumed" the peer's SYN.
    private int _unackedSegments; // Count of in-order full-size data segments not yet acknowledged.
    private long _delayedAckSinceTicks; // When _unackedSegments went 0 -> 1 (drives the delayed-ACK flush).
    private int _ackCount;

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
    private long _retxCount;

    // Dynamic receive-window flow control (all platforms). Bytes written into the net->app reassembly
    // pipe but not yet read by the app (the proxy copy loop). The advertised TCP window is
    // ReceiveWindowSize - _pipeUnread, so it shrinks toward 0 as the pipe fills (throttling the peer)
    // and reopens (with a window-update ACK from OnAppConsumed) as the app drains. TryHandleIncoming
    // additionally ENFORCES the window (drops bytes beyond it), so ReceiveWindowSize is a HARD cap on
    // per-connection pipe memory even against a peer that ignores our advertisements.
    // Written with Interlocked so the lock-free readers (AdvertisedWindow) never see a torn value on 32-bit.
    private long _pipeUnread;
    private bool _windowClosed; // set once we advertised a zero window; guarded by _seqLock
    private ushort _lastAdvertisedWindow = (ushort)options.ReceiveWindowSize;

    // Retransmission ring buffer: holds unacked bytes starting at _sndUna.
    // On loopback we don't normally need retransmission, but TUN/kernel can drop
    // packets under heavy load (no real "loss" but the effect is identical).
    // RFC 5681 fast retransmit: 3 duplicate ACKs trigger retransmit of sndUna segment.
    // PERF: capacity comes from options (RetxBufferSize, default 64 KB). Cached so the ring math
    // (% _retxCapacity) is a field read, identical cost to the former const. Bounds in-flight bytes.
    private readonly int _retxCapacity = options.RetxBufferSize;
    // Lazily allocated on the first send (AppendToRetxBufferLocked) so receive-only / idle flows never
    // pay the per-connection retx allocation — meaningful at MaxConnections under the iOS memory budget.
    // Every direct read of this array is guarded by _retxBufferLen > 0, which implies it is non-null.
    private byte[]? _retxBuffer;
    private int _retxRingStart;     // index in buffer corresponding to _sndUna
    private int _retxBufferLen;     // number of valid unacked bytes
    private uint _lastDupAck;
    private int _dupAckCount;
    private bool _inRecovery; // a retransmission started loss recovery, which ends when SndUna reaches _recoverySeq
    private uint _recoverySeq; // SndNxt when the loss was detected (RFC 6582's recovery point)
    public IPEndPointPairValue IpEndPointPair => ipEndPointPair;
    public uint IsnLocal { get; } = isnLocal;
    public ushort Mss { get; } = ClampMss(peerMss, options);
    public TcpConnectionState State { get; private set; } = TcpConnectionState.SynReceived;

    /// <summary>The 16-bit TCP receive window this connection advertises on every outgoing segment.
    /// DYNAMIC: the configured maximum (the property's backing field, initialized from
    /// <c>ReceiveWindowSize</c>) minus the unread backlog in the reassembly pipe, so it shrinks to 0 as
    /// the pipe fills (real TCP flow control / backpressure) and reopens as the app drains. Bounds
    /// per-connection receive memory on every platform.</summary>
    public ushort AdvertisedWindow {
        get {
            // the configured window minus this connection's unread backlog
            var free = field - Interlocked.Read(ref _pipeUnread);
            if (free <= 0) return 0;
            return free >= field ? field : (ushort)free;
        }
    } = (ushort)options.ReceiveWindowSize;

    // _seqLock must be held. Recomputes the advertised window, records it (for RST validation and
    // window-update deltas) and remembers when it closed so OnAppConsumed knows to re-advertise.
    private ushort UpdateAdvertisedWindowLocked()
    {
        var win = AdvertisedWindow;
        _lastAdvertisedWindow = win;
        if (win == 0)
            _windowClosed = true;
        return win;
    }

    /// <summary>Single-lock snapshot of everything needed to emit a pure ACK or SYN-ACK.</summary>
    internal (uint SndNxt, uint RcvNxt, ushort Window) SnapshotForAck()
    {
        lock (_seqLock)
            return (_sndNxt, _rcvNxt, UpdateAdvertisedWindowLocked());
    }

    /// <summary>
    /// PipeReader for reading data received from network (used by LocalTcpStream)
    /// </summary>
    public PipeReader NetToAppReader => _netToAppPipe.Reader;

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
    /// Wires the stack reference and starts background tasks for this connection.
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

        // Start idle monitor
        _ = Task.Run(MonitorIdleAsync);
    }

    /// <summary>
    /// The app closed its stream, so both directions end. Bytes the app never read, or new bytes the peer
    /// sends afterwards, reset the connection as a kernel does after close() (RFC 1122 4.2.2.13) - once our
    /// own tail data is acknowledged (see <see cref="TryHandleIncoming"/>), so the peer does not drop that
    /// tail unread; otherwise a FIN goes out and the peer's ACK of it ends the connection.
    /// </summary>
    public void CloseFromApp(LocalTcpStack stack)
    {
        bool reset;
        long discarded;
        lock (_seqLock) {
            if (_disposed || _closedFlag) return;
            _readSideClosed = true;
            _appToNetCompleted = true;
            // With our tail data still in flight the RST would make the peer drop it unread (RFC 7230 6.6):
            // then the FIN goes first and the RST follows the ACK of the tail (TryHandleIncoming). The unread
            // bytes leave the pipe total now either way; nobody will read them.
            discarded = Interlocked.Exchange(ref _pipeUnread, 0);
            reset = discarded > 0 && _sndUna == _sndNxt;
            _resetPending = discarded > 0 && !reset;
        }

        if (discarded > 0)
            diagnostics.AddPipeBufferedBytes(-discarded);

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
            if (State != TcpConnectionState.SynReceived) return;
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
                   seqDiff >= 0 && seqDiff <= _lastAdvertisedWindow;
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

        long remainingUnread;
        LocalTcpClient? abandoned;
        bool decrementEstablished;
        lock (_seqLock) {
            if (_disposed) return;
            _disposed = true;
            _finSent = true; // suppress a stray FIN from a later graceful-close after a direct dispose
            remainingUnread = Interlocked.Exchange(ref _pipeUnread, 0);
            abandoned = _pendingClient;
            _pendingClient = null;
            decrementEstablished = _establishedCounted;
            _establishedCounted = false;
            UpdateMaintenanceInterestLocked();
        }

        if (remainingUnread > 0)
            diagnostics.AddPipeBufferedBytes(-remainingUnread);
        if (decrementEstablished)
            diagnostics.DecrementEstablishedConnections(ipEndPointPair, "dispose"); // DIAGNOSTIC (counts + logs)

        // A direct Dispose() — DropAllConnections, LocalTcpStack.Dispose, or a failed _connections.TryAdd —
        // does NOT go through Close(), so it must unblock the app side itself: complete the net->app pipe so
        // a parked ReadAsync returns EOF, dispose any not-yet-accepted client, and release a SendAppDataAsync
        // waiting on the window signal (the connection's _cts is not in the stream's read-cancel set). Without
        // this the proxy copy loop hangs and the connection + its pipe leak. CompleteNetToApp self-locks, so
        // it can never race WriteToAppPipe.
        abandoned?.Dispose();
        CompleteNetToApp();
        _appToNetCompleted = true;
        TrySignalWindow();

        try { _cts.Cancel(); } catch { /* ignore */ }
    }

    /// <summary>
    /// Writes data from app directly as TCP segments to the network (inline on caller's thread).
    /// Respects the peer's advertised receive window. Throws <see cref="IOException"/> when the
    /// connection is (or becomes) closed for writing, and <see cref="OperationCanceledException"/>
    /// on caller cancellation — a partial write is never silently reported as success.
    /// </summary>
    public async ValueTask SendAppDataAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfWriteClosed();
        Touch();

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
                    // missed, which would park it for good on a full ring.
                    if (_disposed || _appToNetCompleted || _finSent)
                        throw new IOException("The TCP connection was closed while writing.");
                    pw = _peerWindow; una = _sndUna; nxt = _sndNxt;
                    var inFlight = (long)(nxt - una);
                    var fromPeer = (int)Math.Max(0, _peerWindow - inFlight);
                    retxFree = _retxCapacity - _retxBufferLen;
                    allowable = Math.Min(fromPeer, retxFree);
                }

                if (allowable <= 0) {
                    diagnostics.TraceZeroWindowWait(ipEndPointPair, offset, data.Length, pw, una, nxt);
                    if (retxFree <= 0) {
                        // The retransmission ring is the limit: a full window is in flight, and its loss is the
                        // RTO's business (PollMaintenance). Wait for the ACK that frees ring space, or the
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

                var packet = PacketBuilder.BuildTcp(IpEndPointPair.Destination, IpEndPointPair.Source,
                    options: ReadOnlySpan<byte>.Empty, payload: segmentData);
                var tcp = packet.ExtractTcp();

                uint seqForSegment;
                uint ackForSegment;
                ushort windowForSegment;
                lock (_seqLock) {
                    // Recheck under the lock: StartFin takes its FIN sequence number under this same
                    // lock, so this guard guarantees no data segment is ever emitted at a sequence
                    // number beyond an already-sent FIN (which the peer would discard = data loss).
                    if (_disposed || _appToNetCompleted || _finSent) {
                        packet.Dispose();
                        throw new IOException("The TCP connection was closed while writing.");
                    }

                    seqForSegment = _sndNxt;
                    ackForSegment = _rcvNxt;
                    // RTO: (re)arm when this segment opens a new in-flight window.
                    if (_sndUna == _sndNxt)
                        RestartRtoLocked();
                    _sndNxt += (uint)segLen;
                    // Append into retx ring buffer for fast retransmit / RTO support.
                    AppendToRetxBufferLocked(segmentData);
                    windowForSegment = UpdateAdvertisedWindowLocked(); // dynamic (flow-controlled) window
                    UpdateMaintenanceInterestLocked();
                }

                tcp.SequenceNumber = seqForSegment;
                tcp.AcknowledgmentNumber = ackForSegment;
                tcp.Acknowledgment = true;
                tcp.WindowSize = windowForSegment;

                // Set PSH on the last segment of the current write.
                if (offset + segLen >= data.Length)
                    tcp.Push = true;

                stack.SendPacket(packet);
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
    /// Handles incoming TCP data from network.
    /// Returns: (handled, needsAck) - handled indicates if packet was processed, needsAck if ACK should be sent
    /// </summary>
    public (bool handled, bool needsAck) TryHandleIncoming(uint seq, uint ack, ushort windowSize, TcpFlags flags, ReadOnlySpan<byte> payload)
    {
        if (_disposed) return (false, false);

        if (flags.HasFlag(TcpFlags.Rst)) {
            // RFC 5961 §3.2: accept an RST only when its sequence number exactly matches RCV.NXT. An
            // in-window (but inexact) RST gets a challenge ACK; anything else is dropped. Without this,
            // any local app able to inject one packet into the TUN could blind-reset arbitrary flows.
            bool exact, inWindow;
            lock (_seqLock) {
                var rstDiff = (int)(seq - _rcvNxt);
                exact = rstDiff == 0;
                inWindow = rstDiff > 0 && rstDiff < Math.Max(1, (int)_lastAdvertisedWindow);
            }

            if (exact) {
                Close();
                return (false, false);
            }
            return inWindow ? (true, true) : (false, false); // challenge ACK / drop
        }

        // After the handshake every segment carries an ACK (RFC 9293 3.10.7.4): one without is dropped with its data.
        if (!flags.HasFlag(TcpFlags.Ack))
            return (false, false);

        // Process the ACK: advance _sndUna and update the peer's window.
        uint newPw;
        long diff;
        uint prevUna;
        uint sndNxtSnap;
        var shouldFastRetx = false;
        var partialAckRetx = false;
        IpPacket? finRetx = null;
        var finExchangeComplete = false;
        var resetUnread = false;
        var challenge = false;
        var acceptable = false;
        lock (_seqLock) {
            prevUna = _sndUna;
            sndNxtSnap = _sndNxt;
            var prevPeerWindow = _peerWindow;
            var scaledWindow = (uint)windowSize << _peerWsShift;
            diff = ack - _sndUna;
            // RFC 9293 3.10.7.4: only a segment inside the receive window with an ack within [SndUna, SndNxt] is
            // processed, carries the peer's window and counts as its activity (a keepalive, one below RcvNxt, too);
            // an ack of data never sent draws a challenge ACK and nothing else (RFC 5961 5.2).
            var ackCurrent = diff <= _sndNxt - _sndUna;
            challenge = !ackCurrent && (int)(ack - _sndNxt) > 0;
            var seqDiff = (int)(seq - _rcvNxt);
            var seqInWindow = seqDiff >= 0
                ? seqDiff <= _lastAdvertisedWindow
                : seqDiff + payload.Length + (flags.HasFlag(TcpFlags.Fin) ? 1 : 0) > 0 || seqDiff == -1 && payload.Length <= 1;
            acceptable = ackCurrent && seqInWindow;
            if (seqInWindow && ackCurrent && diff > 0) {
                _sndUna = ack;
                // Drop acknowledged bytes from retx buffer.
                var n = (int)diff;
                if (n >= _retxBufferLen) {
                    _retxBufferLen = 0;
                    _retxRingStart = 0;
                }
                else {
                    _retxRingStart = (_retxRingStart + n) % _retxCapacity;
                    _retxBufferLen -= n;
                }
                _dupAckCount = 0;
                _lastDupAck = ack;
                RestartRtoLocked(); // cumulative-ACK progress restarts the retransmission timer
                // Loss recovery (RFC 6582 partial ACK): an ACK that stops short of the recovery point names
                // the next lost segment; it is retransmitted now instead of after another RTO.
                if (_inRecovery) {
                    if ((int)(_sndUna - _recoverySeq) >= 0)
                        _inRecovery = false;
                    else if (_retxBufferLen > 0)
                        partialAckRetx = true;
                    else if (_finSent && _sndNxt != _sndUna)
                        // The data is acked and the FIN is the lost one: now, not after the RTO.
                        finRetx = BuildFinPacket(_sndNxt - 1, _rcvNxt, UpdateAdvertisedWindowLocked());
                }
                // LastAck: the peer acknowledged our FIN after its own; nothing is left to exchange.
                finExchangeComplete = _finSent && _finReceived && _sndUna == _sndNxt;
                // The reset CloseFromApp deferred (unread bytes, our tail in flight): everything is acked now.
                resetUnread = _resetPending && _sndUna == _sndNxt;
            }
            else if (seqInWindow && diff == 0 && payload.Length == 0 && _retxBufferLen > 0 && scaledWindow != 0 &&
                     scaledWindow == prevPeerWindow && !flags.HasFlag(TcpFlags.Fin)) {
                // Pure duplicate ACK (RFC 5681: no data, no FIN, UNCHANGED window): the peer is
                // missing data starting at _sndUna. A pure window update (same ack, new window)
                // must NOT count, or three updates would trigger a spurious fast retransmit. Nor may a
                // zero window: its ACKs of our probes are flow control, not loss, and a recovery they
                // started would outlive the window and swallow the real duplicate ACKs after it reopens.
                if (ack == _lastDupAck) _dupAckCount++;
                else { _lastDupAck = ack; _dupAckCount = 1; }
                // Three duplicate ACKs start one recovery; more of them during it retransmit nothing - the
                // partial ACKs above carry the recovery segment by segment.
                if (_dupAckCount >= 3 && !_inRecovery) {
                    shouldFastRetx = true;
                    EnterRecoveryLocked();
                }
            }
            if (acceptable)
                _peerWindow = scaledWindow;
            newPw = _peerWindow;
            UpdateMaintenanceInterestLocked();
        }
        if (challenge)
            return (true, true);
        if (acceptable)
            Touch();
        var ackCount = Interlocked.Increment(ref _ackCount);
        diagnostics.TraceAck(ipEndPointPair, ackCount, ack, prevUna, sndNxtSnap, diff, windowSize,
            newPw, payload.Length, _windowSignal.CurrentCount, _dupAckCount);

        // A failed send below is a lost packet, which the RTO retransmits; the rest of this segment (its
        // window signal, data, FIN) must still be processed.
        if (shouldFastRetx || partialAckRetx) {
            var n = Interlocked.Increment(ref _retxCount);
            diagnostics.TraceFastRetransmit(ipEndPointPair, n, ack, _retxBufferLen);
            try { FastRetransmit(); } catch { /* lost packet */ }
        }
        if (finRetx != null) {
            var stack = _stack ?? throw new InvalidOperationException("The connection was not started.");
            try { stack.SendPacket(finRetx); } catch { /* lost packet */ }
        }
        if (acceptable)
            TrySignalWindow();

        if (resetUnread) {
            Abort();
            return (false, false);
        }
        if (finExchangeComplete) {
            Close();
            // A retransmitted peer FIN on this segment still needs its ACK, or its next retransmit meets
            // an unknown-connection RST.
            return (true, flags.HasFlag(TcpFlags.Fin));
        }

        try {
            bool needsAck;
            var processFin = false;
            var finCloses = false;
            var dataAfterClose = false;
            var resetAfterClose = false;

            lock (_seqLock) {
                var seqDiff = (int)(seq - _rcvNxt);

                if (seqDiff > 0) {
                    // Out of order - send duplicate ACK to trigger fast retransmit
                    return (true, true);
                }

                // seqDiff <= 0: consume any bytes beyond _rcvNxt. This single path covers both the
                // in-order case (seqDiff == 0) and a partially-overlapping retransmission (seqDiff < 0).
                var isRetransmit = seqDiff < 0;
                var overlap = -seqDiff; // already-received prefix of this segment
                var truncated = false;
                var acceptedNew = 0;
                // New bytes after the app closed its stream have no reader: reset below, as a kernel does for
                // data after close(), instead of buffering them into a dead pipe that pins the connection. While
                // our own FIN or tail data is unacknowledged they are dropped without an ACK instead (a RST now
                // would make the peer drop that tail unread); its retransmit draws the RST once all is acked.
                dataAfterClose = payload.Length > overlap && _readSideClosed;
                if (dataAfterClose) {
                    resetAfterClose = _sndUna == _sndNxt;
                }
                else if (payload.Length > overlap) {
                    var newData = payload[overlap..];
                    // HARD receive-window enforcement: never buffer beyond the configured window, even if
                    // the peer ignores our advertisements. Real TCP receivers drop out-of-window bytes the
                    // same way; a compliant peer never hits this, a hostile one retransmits into a bounded
                    // buffer instead of exhausting memory. (This is also what keeps the reassembly pipe
                    // below its pause threshold — see LocalTcpStackOptions.CreatePipeOptions.)
                    var headroom = (int)Math.Clamp(
                        options.ReceiveWindowSize - Interlocked.Read(ref _pipeUnread), 0, int.MaxValue);
                    acceptedNew = Math.Min(newData.Length, headroom);
                    if (acceptedNew > 0) {
                        WriteToAppPipe(newData[..acceptedNew]);
                        _rcvNxt += (uint)acceptedNew;
                    }
                    truncated = acceptedNew < newData.Length;
                }

                needsAck = isRetransmit || acceptedNew > 0 || truncated;

                // Allocation-free delayed ACK policy:
                // - ACK PSH/FIN, short segments, retransmits and truncations immediately.
                // - For clean in-order full-size data, ACK every 2nd segment to avoid flooding the iOS
                //   packet path. A pending (thinned) ACK is flushed by the maintenance sweep within
                //   DelayedAckTimeout so an odd trailing segment never waits for the peer's RTO.
                if (needsAck && !isRetransmit && !truncated &&
                    !flags.HasFlag(TcpFlags.Fin) && !flags.HasFlag(TcpFlags.Psh) &&
                    payload.Length >= Mss) {
                    _unackedSegments++;
                    if (_unackedSegments < 2) {
                        needsAck = false;
                        _delayedAckSinceTicks = Stopwatch.GetTimestamp();
                    }
                    else {
                        _unackedSegments = 0;
                    }
                }
                else if (needsAck) {
                    _unackedSegments = 0;
                }

                // FIN consumes _rcvNxt only once every payload byte before it has been consumed. Checking
                // the FIN's own sequence position (seq + payload length) instead of "seqDiff == 0" also
                // processes a FIN that arrives inside a partially-overlapping retransmitted segment —
                // previously such a FIN was silently ignored and the close waited for a pure-FIN retransmit.
                if (flags.HasFlag(TcpFlags.Fin) && !_finReceived && seq + (uint)payload.Length == _rcvNxt) {
                    _rcvNxt += 1;
                    _finReceived = true;
                    needsAck = true;
                    processFin = true;

                    // Both FINs exchanged: closed once ours is acknowledged too (this segment's ACK was
                    // processed above), otherwise LastAck retransmits it until the ACK arrives.
                    if (_finSent)
                        State = _sndUna == _sndNxt ? TcpConnectionState.Closed : TcpConnectionState.LastAck;
                    else
                        State = TcpConnectionState.Closing;
                    finCloses = State == TcpConnectionState.Closed;
                }

                UpdateMaintenanceInterestLocked();
            }

            if (dataAfterClose) {
                if (resetAfterClose)
                    Abort();
                return (false, false);
            }

            if (processFin) {
                CompleteNetToApp();
                if (finCloses)
                    Close();
            }

            return (true, needsAck);
        }
        catch (InvalidOperationException) {
            // Pipe was completed/broken - abort (the peer gets an RST instead of a silent black hole)
            Abort();
            return (false, false);
        }
    }

    private void WriteToAppPipe(ReadOnlySpan<byte> data)
    {
        // _seqLock is already held by the caller of WriteToAppPipe.
        if (_disposed || _netToAppCompleted) return;

        var span = _netToAppPipe.Writer.GetSpan(data.Length);
        data.CopyTo(span);
        _netToAppPipe.Writer.Advance(data.Length);
        diagnostics.AddPipeBufferedBytes(data.Length); // the stack-wide unread total, a diagnostic

        // FLOW CONTROL: track the unread backlog so AdvertisedWindow shrinks as the pipe fills. If the
        // window is now 0 (this pipe is full), mark it closed so OnAppConsumed sends a window-update ACK
        // once the app drains.
        Interlocked.Add(ref _pipeUnread, data.Length);
        if (AdvertisedWindow == 0)
            _windowClosed = true;

        // Flush is fire-and-forget on this packet-receive critical path. That is safe because the caller
        // (TryHandleIncoming) hard-enforces the advertised window, so the unread backlog never exceeds
        // ReceiveWindowSize — which sits BELOW the pipe's pause threshold (ReceiveWindowSize + MaxMss).
        // FlushAsync therefore always completes synchronously; discarding a completed ValueTask is a
        // plain struct drop, never a pending IValueTaskSource (which must not be abandoned).
        _ = _netToAppPipe.Writer.FlushAsync();
    }

    /// Called by LocalTcpStream after the app drains bytes from the reassembly pipe. This is a
    /// FLOW-CONTROL step, not a diagnostic: lowering the unread backlog is what reopens
    /// AdvertisedWindow, and this method is the one sender of the window-update ACK that lets a
    /// throttled peer resume immediately instead of waiting for its zero-window-probe timer.
    public void OnAppConsumed(long count)
    {
        if (count <= 0) return;

        long actualConsumed;
        var sendUpdate = false;
        lock (_seqLock) {
            if (_disposed) return;
            var unread = Interlocked.Read(ref _pipeUnread);
            actualConsumed = Math.Min(unread, count);
            Interlocked.Add(ref _pipeUnread, -actualConsumed);
            var currentWin = AdvertisedWindow;
            var lastWin = _lastAdvertisedWindow;

            // Send a window update ACK if:
            // 1. The window was closed and has reopened to at least _windowReopenFloor.
            // 2. Or the window has opened up by at least _windowUpdateThreshold (~1/4 window) to prevent
            //    stalls and keep the peer's window sliding smoothly.
            // The decision AND the flag-clear both run under _seqLock so a concurrent WriteToAppPipe that
            // just re-closed the window can never be overwritten by a stale snapshot.
            var isReopened = currentWin >= _windowReopenFloor;
            var windowOpenedSignificantly = currentWin - lastWin >= _windowUpdateThreshold;
            if ((_windowClosed && isReopened) || windowOpenedSignificantly) {
                if (isReopened)
                    _windowClosed = false;
                sendUpdate = true;
            }
        }

        if (actualConsumed > 0)
            diagnostics.AddPipeBufferedBytes(-actualConsumed);

        if (sendUpdate)
            try { _stack?.SendAckOnly(this); } catch { /* best-effort window update */ }
    }

    private void Touch()
    {
        Interlocked.Exchange(ref _lastActivityTicks, Stopwatch.GetTimestamp());
    }

    /// <summary>Time since the last send/receive activity on this connection (drives LRU eviction). For an
    /// orphan (the app closed its stream) it is the time since the sequence numbers last moved, sampled by
    /// the idle monitor, so a peer's no-progress ACKs cannot keep it from eviction.</summary>
    internal TimeSpan IdleDuration {
        get {
            var progress = Volatile.Read(ref _orphanProgressTicks);
            return Stopwatch.GetElapsedTime(progress != 0 ? progress : Interlocked.Read(ref _lastActivityTicks));
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
    // (SYN-ACK retransmission, RTO, delayed-ACK flush). Interest is held exactly while there is
    // something the sweep could act on, so a fully idle stack keeps its maintenance timer parked
    // (no periodic wake-ups — battery matters on mobile).
    private void UpdateMaintenanceInterestLocked()
    {
        var stack = _stack;
        if (stack == null) return; // Start() registers the initial interest once wired

        var desired = !_disposed && !_closedFlag &&
                      (State == TcpConnectionState.SynReceived || _sndNxt != _sndUna || _unackedSegments > 0);
        if (desired == _maintenanceInterestHeld) return;
        _maintenanceInterestHeld = desired;
        if (desired) stack.AddMaintenanceInterest();
        else stack.RemoveMaintenanceInterest();
    }

    // _seqLock must be held.
    private void RestartRtoLocked()
    {
        _rtoStartTicks = Stopwatch.GetTimestamp();
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
                    if (_retxBufferLen > 0) {
                        retxData = true;
                        // The ACKs of this retransmission carry the rest segment by segment - unless the peer's
                        // window is zero: that is flow control, not loss, and a recovery the zero window
                        // interrupted ends here so the duplicate ACKs after it reopens count again.
                        if (_peerWindow != 0) EnterRecoveryLocked();
                        else _inRecovery = false;
                    }
                    else if (_finSent)
                        // All data is acked; the single outstanding sequence number is the FIN.
                        finRetx = BuildFinPacket(_sndNxt - 1, _rcvNxt, UpdateAdvertisedWindowLocked());
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
        if (retxData) {
            var n = Interlocked.Increment(ref _retxCount);
            diagnostics.TraceFastRetransmit(ipEndPointPair, n, _lastDupAck, _retxBufferLen);
            try { FastRetransmit(); } catch { /* next sweep retries */ }
        }
        if (finRetx != null)
            try { stack.SendPacket(finRetx); } catch { /* SendPacket disposes the packet on failure */ }
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

    private async Task MonitorIdleAsync()
    {
        try {
            using var timer = new PeriodicTimer(_idleCheckInterval);
            var orphanTracked = false;
            uint progressUna = 0;
            uint progressRcv = 0;
            while (await timer.WaitForNextTickAsync(_cts.Token).Vhc()) {
                if (_disposed || State == TcpConnectionState.Closed)
                    break;

                if (_readSideClosed) {
                    // An orphan (the app closed its stream) is idle when the sequence numbers stop moving: the
                    // peer's window-0 ACKs of our retransmits or its keepalive probes would otherwise keep it
                    // "active" and pin its slot forever. Sampled per tick, never per packet; IdleDuration (the
                    // eviction's measure too) reads the sample.
                    uint una, rcv;
                    lock (_seqLock) {
                        una = _sndUna;
                        rcv = _rcvNxt;
                    }
                    if (!orphanTracked || una != progressUna || rcv != progressRcv) {
                        orphanTracked = true;
                        progressUna = una;
                        progressRcv = rcv;
                        Volatile.Write(ref _orphanProgressTicks, Stopwatch.GetTimestamp());
                    }
                }

                if (IdleDuration >= _idleTimeout) {
                    // Abortive: tell the peer with an RST so it doesn't linger on a black-holed flow.
                    Abort();
                    break;
                }
            }
        }
        catch (OperationCanceledException) {
            // Expected on disposal/close
        }
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
                RestartRtoLocked(); // the FIN opens a new in-flight window; arm its retransmission
            _sndNxt += 1; // FIN consumes one sequence number
            finWindow = UpdateAdvertisedWindowLocked(); // dynamic (flow-controlled) window

            // The connection ends only when the peer acknowledges this FIN (TryHandleIncoming's ACK path);
            // until then PollMaintenance retransmits it, and the idle timeout bounds a peer that never does.
            State = _finReceived ? TcpConnectionState.LastAck : TcpConnectionState.FinWait1;
            UpdateMaintenanceInterestLocked();
        }

        // Built outside _seqLock: PacketBuilder rents pooled memory and needs no sequence state.
        var finPacket = BuildFinPacket(finSeq, finAck, finWindow);
        stack.SendPacket(finPacket);
    }

    private IpPacket BuildFinPacket(uint seq, uint ackNum, ushort window)
    {
        var packet = PacketBuilder.BuildTcp(
            IpEndPointPair.Destination, IpEndPointPair.Source,
            options: ReadOnlySpan<byte>.Empty,
            payload: ReadOnlySpan<byte>.Empty);

        var tcp = packet.ExtractTcp();
        tcp.SequenceNumber = seq;
        tcp.AcknowledgmentNumber = ackNum;
        tcp.Finish = true;
        tcp.Acknowledgment = true;
        tcp.WindowSize = window;
        return packet;
    }

    public void TryStartFin(LocalTcpStack stack)
    {
        try { StartFin(stack); } catch { /* ignore */ }
    }

    private void CompleteNetToApp()
    {
        // Take _seqLock so writer completion is serialized against WriteToAppPipe (which runs under the
        // same lock). Otherwise Close()/Dispose() from the idle-timer or drop/dispose threads could call
        // Writer.Complete() concurrently with a receive-thread GetSpan/Advance — which System.IO.Pipelines
        // forbids. The pipe uses the ThreadPool scheduler, so Complete() never resumes the reader inline,
        // and System.Threading.Lock is reentrant, so callers already holding the lock remain safe.
        lock (_seqLock) {
            if (_netToAppCompleted) return;
            _netToAppCompleted = true;
            try { _netToAppPipe.Writer.Complete(); } catch { /* already completed */ }
        }
    }

    private void TrySignalWindow()
    {
        if (_windowSignal.CurrentCount == 0) {
            try { _windowSignal.Release(); }
            catch (SemaphoreFullException) { /* already signalled */ }
        }
    }

    /// <summary>
    /// Zero-window probe: one NEW byte at SndNxt, stored in the ring like any data. A probe that does not
    /// advance the sequence number draws no window update over QUIC's tight per-stream window (see
    /// ZeroWindowProbe_ShouldMakeForwardProgress). Returns the bytes consumed: 1, or 0 when nothing could
    /// be sent (the connection is closing, or the ring has no room - the caller probes only while it has).
    /// </summary>
    private int SendZeroWindowProbe(LocalTcpStack stack, byte probeByte)
    {
        if (_disposed || _appToNetCompleted) return 0;

        uint probeSeq;
        uint probeAck;
        ushort probeWindow;
        lock (_seqLock) {
            if (_disposed || _appToNetCompleted || _finSent || _retxBufferLen >= _retxCapacity) return 0;

            probeSeq = _sndNxt;
            if (_sndUna == _sndNxt)
                RestartRtoLocked();
            _sndNxt += 1;
            ReadOnlySpan<byte> one = [probeByte];
            AppendToRetxBufferLocked(one);
            probeAck = _rcvNxt;
            probeWindow = UpdateAdvertisedWindowLocked(); // dynamic (flow-controlled) window
            UpdateMaintenanceInterestLocked();
        }

        IpPacket? probe = null;
        try {
            ReadOnlySpan<byte> probeData = [probeByte];
            probe = PacketBuilder.BuildTcp(IpEndPointPair.Destination, IpEndPointPair.Source,
                options: ReadOnlySpan<byte>.Empty, payload: probeData);
            var tcp = probe.ExtractTcp();
            tcp.SequenceNumber = probeSeq;
            tcp.AcknowledgmentNumber = probeAck;
            tcp.Acknowledgment = true;
            tcp.WindowSize = probeWindow;

            var toSend = probe;
            probe = null; // SendPacket owns it from here (it disposes on its own failure)
            stack.SendPacket(toSend);
        }
        catch {
            // Sequence state is already advanced and the consumed byte is stored in the retx ring, so
            // a failed send equals a dropped packet: the RTO sweep retransmits it. Returning 1 (not 0)
            // keeps the caller's offset in sync with _sndNxt — returning 0 after advancing _sndNxt
            // used to duplicate the byte in the stream.
            probe?.Dispose();
        }
        return 1;
    }

    private void DrainWindowSignal()
    {
        while (_windowSignal.Wait(0)) { }
    }

    // _seqLock must be held.
    private void AppendToRetxBufferLocked(ReadOnlySpan<byte> segment)
    {
        if (segment.Length == 0) return;
        // Ring/sequence INVARIANT: ring offset k always holds the byte at sequence _sndUna + k (the
        // ACK-trim and FastRetransmit both rely on it). Every caller must room-check first; overflowing
        // would wrap over unacked bytes and silently corrupt the retransmitted stream.
        Debug.Assert(segment.Length <= _retxCapacity - _retxBufferLen,
            "retx ring overflow: caller must enforce capacity before appending");
        // Lazy allocation of the ring buffer to avoid memory footprint spikes under high connection
        // counts: we only allocate the buffer when data is actually sent.
        _retxBuffer ??= new byte[_retxCapacity];
        var writeIdx = (_retxRingStart + _retxBufferLen) % _retxCapacity;
        var firstChunk = Math.Min(segment.Length, _retxCapacity - writeIdx);
        segment[..firstChunk].CopyTo(_retxBuffer.AsSpan(writeIdx));
        if (firstChunk < segment.Length)
            segment[firstChunk..].CopyTo(_retxBuffer.AsSpan(0));
        _retxBufferLen += segment.Length;
    }

    private void FastRetransmit()
    {
        var stack = _stack;
        if (stack == null || _disposed) return;

        // Build retransmit segment of up to MSS bytes from start of retx buffer.
        byte[] payloadCopy;
        uint seqForSegment;
        uint ackForSegment;
        ushort windowForSegment;
        lock (_seqLock) {
            if (_retxBufferLen == 0) return;
            var segLen = Math.Min(_retxBufferLen, Mss);
            payloadCopy = new byte[segLen];
            var firstChunk = Math.Min(segLen, _retxCapacity - _retxRingStart);
            Array.Copy(_retxBuffer!, _retxRingStart, payloadCopy, 0, firstChunk);
            if (firstChunk < segLen)
                Array.Copy(_retxBuffer!, 0, payloadCopy, firstChunk, segLen - firstChunk);
            seqForSegment = _sndUna;
            ackForSegment = _rcvNxt;
            windowForSegment = UpdateAdvertisedWindowLocked(); // dynamic (flow-controlled) window
        }

        var packet = PacketBuilder.BuildTcp(IpEndPointPair.Destination, IpEndPointPair.Source,
            options: ReadOnlySpan<byte>.Empty, payload: payloadCopy);
        var tcp = packet.ExtractTcp();
        tcp.SequenceNumber = seqForSegment;
        tcp.AcknowledgmentNumber = ackForSegment;
        tcp.Acknowledgment = true;
        tcp.WindowSize = windowForSegment;
        tcp.Push = true;

        stack.SendPacket(packet);
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
        if (Interlocked.Exchange(ref _closedFlag, true))
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

    private void TrySendRst(uint seq, uint ackNum)
    {
        var stack = _stack;
        if (stack == null) return;
        try {
            var packet = PacketBuilder.BuildTcp(IpEndPointPair.Destination, IpEndPointPair.Source,
                options: ReadOnlySpan<byte>.Empty, payload: ReadOnlySpan<byte>.Empty);
            var tcp = packet.ExtractTcp();
            tcp.SequenceNumber = seq;
            tcp.AcknowledgmentNumber = ackNum;
            tcp.Reset = true;
            tcp.Acknowledgment = true;
            stack.SendPacket(packet);
        }
        catch { /* best-effort notification */ }
    }
}
