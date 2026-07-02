 using System.Diagnostics;
using System.IO.Pipelines;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.TcpStack.Abstractions;
using VpnHood.Core.TcpStack.Primitives;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo

namespace VpnHood.Core.TcpStack;

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

    // Aggregate cap on the SUM of every connection's unread reassembly-pipe backlog. AdvertisedWindow
    // clamps each connection's window by the remaining global headroom (see LocalTcpStackOptions).
    private readonly long _globalReceiveBudget = options.GlobalReceiveBudget;

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
    private volatile bool _windowClosed; // set once we've advertised a (near-)zero window
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
            // Per-connection headroom: the configured window minus this connection's unread backlog.
            var perConnFree = field - Interlocked.Read(ref _pipeUnread);
            if (perConnFree <= 0) return 0;
            // Global headroom: the shared budget minus the total backlog across ALL connections. This
            // is what keeps a large per-connection window safe when many flows are active at once.
            var globalFree = _globalReceiveBudget - diagnostics.TotalPipeBufferedBytes;
            var free = perConnFree < globalFree ? perConnFree : globalFree;
            if (free <= 0) return 0;
            return free >= field ? field : (ushort)free;
        }
    } = (ushort)options.ReceiveWindowSize;

    // _seqLock must be held. Recomputes the advertised window, records it (for RST validation and
    // window-update deltas) and remembers when it closed so OnAppConsumed / the window sweep know to
    // re-advertise. A zero can happen on ANY send path (e.g. the global budget emptied between drains),
    // not just when this connection's own pipe fills.
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
    /// Gracefully closes the connection: marks the app→net direction as complete, then sends FIN.
    /// Used by <see cref="LocalTcpStream.DisposeAsync"/> so closing the stream does not truncate data.
    /// </summary>
    public Task GracefulCloseAsync(LocalTcpStack stack)
    {
        if (_disposed) return Task.CompletedTask;
        _appToNetCompleted = true;
        TryStartFin(stack);
        return Task.CompletedTask;
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
            // Listener stopped or its accept queue is full: abort so the peer gets a RST immediately
            // instead of a fully-established connection that lingers half-closed buffering data it can
            // never deliver, then dispose the unaccepted client (its FIN attempt becomes a no-op).
            Abort();
            clientToEnqueue.Dispose();
        }
    }

    public bool IsValidHandshakeAck(uint seq, uint ack, TcpFlags flags)
    {
        if (!flags.HasFlag(TcpFlags.Ack))
            return false;

        lock (_seqLock) {
            return State == TcpConnectionState.SynReceived &&
                   seq == _rcvNxt &&
                   ack == _sndNxt;
        }
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
                lock (_seqLock) {
                    pw = _peerWindow; una = _sndUna; nxt = _sndNxt;
                    var inFlight = (long)(nxt - una);
                    var fromPeer = (int)Math.Max(0, _peerWindow - inFlight);
                    var retxFree = _retxCapacity - _retxBufferLen;
                    allowable = Math.Min(fromPeer, retxFree);
                }

                if (allowable <= 0) {
                    diagnostics.TraceZeroWindowWait(ipEndPointPair, offset, data.Length, pw, una, nxt);
                    // Wait for peer window (or retx-ring space) to open — signalled by TrySignalWindow
                    // when an ACK arrives. On timeout send a Zero Window Probe (ZWP) as a stimulus: this
                    // avoids a permanent stall when the peer (e.g. Android) expects one before it sends
                    // the window update. Caller cancellation propagates as OperationCanceledException —
                    // a partial write must never be silently reported as success.
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
        Touch();

        if (flags.HasFlag(TcpFlags.Rst)) {
            // RFC 5961 §3.2: accept a RST only when its sequence number exactly matches RCV.NXT. An
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

        // Process ACK: advance _sndUna and update peer's window.
        if (flags.HasFlag(TcpFlags.Ack)) {
            uint newPw;
            long diff;
            uint prevUna;
            uint sndNxtSnap;
            var shouldFastRetx = false;
            lock (_seqLock) {
                prevUna = _sndUna;
                sndNxtSnap = _sndNxt;
                var prevPeerWindow = _peerWindow;
                var scaledWindow = (uint)windowSize << _peerWsShift;
                // Only advance if ack is within [_sndUna, _sndNxt].
                diff = ack - _sndUna;
                if (diff > 0 && diff <= _sndNxt - _sndUna) {
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
                }
                else if (diff == 0 && payload.Length == 0 && _retxBufferLen > 0 &&
                         scaledWindow == prevPeerWindow && !flags.HasFlag(TcpFlags.Fin)) {
                    // Pure duplicate ACK (RFC 5681: no data, no FIN, UNCHANGED window): the peer is
                    // missing data starting at _sndUna. A pure window update (same ack, new window)
                    // must NOT count, or three updates would trigger a spurious fast retransmit.
                    if (ack == _lastDupAck) _dupAckCount++;
                    else { _lastDupAck = ack; _dupAckCount = 1; }
                    if (_dupAckCount >= 3) {
                        shouldFastRetx = true;
                        _dupAckCount = 0; // avoid retx flood; reset until next dup-ack triple
                    }
                }
                _peerWindow = scaledWindow;
                newPw = _peerWindow;
                UpdateMaintenanceInterestLocked();
            }
            var ackCount = Interlocked.Increment(ref _ackCount);
            diagnostics.TraceAck(ipEndPointPair, ackCount, ack, prevUna, sndNxtSnap, diff, windowSize,
                newPw, payload.Length, _windowSignal.CurrentCount, _dupAckCount);

            if (shouldFastRetx) {
                var n = Interlocked.Increment(ref _retxCount);
                diagnostics.TraceFastRetransmit(ipEndPointPair, n, ack, _retxBufferLen);
                FastRetransmit();
            }
            TrySignalWindow();
        }

        try {
            bool needsAck;
            var processFin = false;
            var finCloses = false;

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
                if (payload.Length > overlap) {
                    var newData = payload[overlap..];
                    // HARD receive-window enforcement: never buffer beyond the configured window, even if
                    // the peer ignores our advertisements. Real TCP receivers drop out-of-window bytes the
                    // same way; a compliant peer never hits this, a hostile one retransmits into a bounded
                    // buffer instead of exhausting memory. (This is also what keeps the reassembly pipe
                    // below its pause threshold — see LocalTcpStackOptions.CreatePipeOptions.)
                    var headroom = (int)Math.Clamp(
                        (long)options.ReceiveWindowSize - Interlocked.Read(ref _pipeUnread), 0, int.MaxValue);
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

                    // Check if both sides have sent FIN
                    if (_finSent) {
                        State = TcpConnectionState.Closed;
                        finCloses = true;
                    }
                    else {
                        State = TcpConnectionState.Closing;
                    }
                }

                UpdateMaintenanceInterestLocked();
            }

            if (processFin) {
                CompleteNetToApp();
                if (finCloses)
                    Close();
            }

            return (true, needsAck);
        }
        catch (InvalidOperationException) {
            // Pipe was completed/broken - abort (the peer gets a RST instead of a silent black hole)
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
        // FLOW CONTROL input, not just a metric: feeds the shared GlobalReceiveBudget headroom in
        // AdvertisedWindow across ALL connections.
        diagnostics.AddPipeBufferedBytes(data.Length);

        // FLOW CONTROL: track the unread backlog so AdvertisedWindow shrinks as the pipe fills. If the
        // effective window is now 0 (this pipe is full OR the shared global budget is exhausted), mark
        // it closed so OnAppConsumed sends a window-update ACK once headroom returns.
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
    /// AdvertisedWindow, and this method is the primary sender of the window-update ACK that lets a
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
            var pipeUnreadSnap = unread - actualConsumed;
            var currentWin = AdvertisedWindow;
            var lastWin = _lastAdvertisedWindow;

            // Send a window update ACK if:
            // 1. The window was closed (or near-closed) and has reopened to at least effectiveReopenFloor.
            // 2. Or the window has opened up by at least _windowUpdateThreshold (~1/4 window) to prevent
            //    stalls and keep the peer's window sliding smoothly.
            // Under a tight global receive budget, the maximum possible window we can currently advertise
            // might be constrained below _windowReopenFloor. If the connection's buffer is fully drained,
            // we should send a reopen update for whatever space is actually available rather than stay closed.
            var maxConnFree = (long)options.ReceiveWindowSize;
            var globalFree = _globalReceiveBudget - diagnostics.TotalPipeBufferedBytes;
            var maxPossible = Math.Max(0, Math.Min(maxConnFree, globalFree));
            var effectiveReopenFloor = Math.Min(_windowReopenFloor, maxPossible);

            // Reopen-floor budget starvation: keep _windowClosed = true while currentWin is still below the
            // real floor (a tiny slot under budget throttling), so subsequent drains keep sending updates
            // immediately; only clear it once the window is genuinely back above _windowReopenFloor.
            // The decision AND the flag-clear both run under _seqLock so a concurrent WriteToAppPipe that
            // just re-closed the window can never be overwritten by a stale snapshot.
            var isReopened = (currentWin >= effectiveReopenFloor || pipeUnreadSnap == 0) && currentWin > 0;
            var windowOpenedSignificantly = currentWin - lastWin >= _windowUpdateThreshold;
            if ((_windowClosed && isReopened) || windowOpenedSignificantly) {
                if (currentWin >= _windowReopenFloor)
                    _windowClosed = false;
                sendUpdate = true;
            }
        }

        if (actualConsumed > 0)
            diagnostics.AddPipeBufferedBytes(-actualConsumed);

        if (sendUpdate)
            try { _stack?.SendAckOnly(this); } catch { /* best-effort window update */ }
    }

    /// <summary>
    /// Periodic re-advertisement of a receive window that closed under flow control and has since
    /// reopened, for a flow whose app has gone quiet. <see cref="OnAppConsumed"/> is the only other
    /// path that sends a window-update ACK, and it only fires while THIS flow is draining bytes. But
    /// the window can also reopen because OTHER flows drained and freed the shared
    /// <see cref="LocalTcpStackOptions.GlobalReceiveBudget"/> — at which point this flow advertises
    /// a healthy window again but, with its own app stalled at the zero-window we sent, no drain
    /// occurs to deliver that update. The peer then stays frozen until its own zero-window persist
    /// timer expires (hundreds of ms to seconds) — the "upload freezes then suddenly resumes" stall.
    /// The stack's window sweep calls this so the update goes out within a sweep interval instead.
    /// Returns true if a window-update ACK was sent.
    /// </summary>
    internal bool PollWindowReopen()
    {
        // Lock-free pre-check: only a flow we've throttled to (near-)zero is a candidate.
        // (_appToNetCompleted is deliberately NOT checked: it describes the SEND direction, while this
        // services the RECEIVE window — a half-closed flow can still be uploading to us.)
        if (!_windowClosed || _disposed)
            return false;

        bool reopened;
        lock (_seqLock) {
            if (!_windowClosed || _disposed)
                return false;
            // Only re-advertise once the window is genuinely back above the real floor — not a tiny
            // slot still pinned by per-connection backlog or a tight global budget (matches the
            // _windowClosed-clear condition in OnAppConsumed). If it's still small, stay closed and
            // let a later sweep (or an OnAppConsumed drain) carry it.
            reopened = AdvertisedWindow >= _windowReopenFloor;
            if (reopened)
                _windowClosed = false;
        }

        if (!reopened)
            return false;

        try { _stack?.SendAckOnly(this); } catch { /* best-effort window update */ }
        return true;
    }

    private void Touch()
    {
        Interlocked.Exchange(ref _lastActivityTicks, Stopwatch.GetTimestamp());
    }

    /// <summary>Time since the last send/receive activity on this connection (drives LRU eviction).</summary>
    internal TimeSpan IdleDuration => Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastActivityTicks));

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
    ///     strands server-speaks-first connections in SynReceived until the idle timeout;
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
                    resendSynAck = true;
                    BackoffRtoLocked();
                }
                else if (_sndNxt != _sndUna) {
                    if (_retxBufferLen > 0)
                        retxData = true;
                    else if (_finSent)
                        // All data is acked; the single outstanding sequence number is the FIN.
                        finRetx = BuildFinPacket(_sndNxt - 1, _rcvNxt, UpdateAdvertisedWindowLocked());
                    BackoffRtoLocked();
                }
            }

            UpdateMaintenanceInterestLocked();
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
            while (await timer.WaitForNextTickAsync(_cts.Token).Vhc()) {
                if (_disposed || State == TcpConnectionState.Closed)
                    break;

                var last = Interlocked.Read(ref _lastActivityTicks);
                var elapsed = Stopwatch.GetElapsedTime(last);
                if (elapsed >= _idleTimeout) {
                    // Abortive: tell the peer with a RST so it doesn't linger on a black-holed flow.
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
        bool closeAfter;

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

            closeAfter = _finReceived;
            State = closeAfter ? TcpConnectionState.Closed : TcpConnectionState.FinWait1;
            UpdateMaintenanceInterestLocked();
        }

        // Built outside _seqLock: PacketBuilder rents pooled memory and needs no sequence state.
        var finPacket = BuildFinPacket(finSeq, finAck, finWindow);
        stack.SendPacket(finPacket);

        if (closeAfter)
            Close();
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
    /// Sends a Zero Window Probe to elicit a window-update ACK from a peer that advertised a zero
    /// (or too-small) window, or when the retx ring is full and ACKs have gone silent.
    /// INVARIANT (forward progress — do NOT remove): while the retx ring HAS ROOM, a probe carries a
    /// NEW byte at _sndNxt, advances _sndNxt, stores the byte in the ring, and returns consumed = 1.
    /// A probe that does not advance the sequence number never elicits a window-update ACK over QUIC's
    /// tight per-stream window (the QUIC+TcpProxy download regression — see
    /// ZeroWindowProbe_ShouldMakeForwardProgress and the quic-proxy-download-regression record).
    /// INVARIANT (ring/sequence integrity — do NOT remove): a byte may only be consumed if it is stored
    /// in the retx ring. When the ring is FULL the probe consumes nothing and instead retransmits up to
    /// a full MSS from _sndUna (via <see cref="FastRetransmit"/>) and returns consumed = 0.
    /// INVARIANT (segment-granularity recovery — do NOT shrink to 1 byte): the ring-full stimulus must
    /// be a FULL SEGMENT, not the single byte at _sndUna. Under burst loss (a whole in-flight window
    /// dropped by the TUN at peak rate) the writer parks on a full ring with more data pending, and the
    /// probe interval (200 ms) beats the RTO (500 ms). A 1-byte probe then fills the hole one byte at a
    /// time; each +1 cumulative ACK RESTARTS the RTO and resets the dup-ACK count, so neither the RTO
    /// nor fast retransmit ever fires and the flow crawls at ~2 bytes per probe interval while looking
    /// "active" (never idle-reaped, pinning its connection slot and QUIC window credit) — the
    /// speedtest "collapse to ~1 then slowly recover" incident (2026-07-01, see
    /// BurstLoss_RingFullProbe_RecoversAtSegmentGranularity).
    /// </summary>
    private int SendZeroWindowProbe(LocalTcpStack stack, byte probeByte)
    {
        if (_disposed || _appToNetCompleted) return 0;

        var ringFull = false;
        uint probeSeq = 0;
        uint probeAck = 0;
        ushort probeWindow = 0;
        lock (_seqLock) {
            if (_disposed || _appToNetCompleted || _finSent) return 0;

            if (_retxBufferLen < _retxCapacity) {
                // Room in the ring: consume (and store) a fresh byte so the stream makes forward progress.
                probeSeq = _sndNxt;
                if (_sndUna == _sndNxt)
                    RestartRtoLocked();
                _sndNxt += 1;
                ReadOnlySpan<byte> one = [probeByte];
                AppendToRetxBufferLocked(one);
                probeAck = _rcvNxt;
                probeWindow = UpdateAdvertisedWindowLocked(); // dynamic (flow-controlled) window
            }
            else {
                // Ring full: never consume a byte we cannot back. Retransmit a full segment from
                // _sndUna instead (below, outside this lock) — still a valid probe stimulus (the peer
                // answers with an ACK carrying its window) AND real recovery at MSS granularity (see
                // the segment-granularity invariant above).
                ringFull = true;
            }

            UpdateMaintenanceInterestLocked();
        }

        if (ringFull) {
            // FastRetransmit re-takes _seqLock itself and no-ops if a racing ACK just drained the ring.
            var n = Interlocked.Increment(ref _retxCount);
            diagnostics.TraceFastRetransmit(ipEndPointPair, n, _lastDupAck, _retxBufferLen);
            try { FastRetransmit(); } catch { /* best-effort; the next probe interval retries */ }
            return 0;
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
    /// tears the connection down AND tells the peer with a RST, so it does not linger retransmitting
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
            rstSeq = _sndNxt;
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
