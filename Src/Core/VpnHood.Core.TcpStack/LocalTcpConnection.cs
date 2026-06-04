using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.TcpStack.Abstractions;
using VpnHood.Core.TcpStack.Primitives;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

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
    PipeOptions pipeOptions)
    : IDisposable
{
    // All memory/timeout sizing comes from the stack's validated options (see LocalTcpStackOptions).
    // The values used on the data path below are cached into readonly fields once, here, so the
    // hot path (send/recv/ACK) costs exactly what the old consts did — a field read, no recompute.

    // PERF: advertised TCP receive window. Defaults to 65535; must stay equal to the historical
    // const on Android (default options) to preserve throughput. Fits the 16-bit window field
    // because LocalTcpStackOptions validates ReceiveWindowSize <= 65535 (no window scaling).
    private readonly ushort _advertisedWindow = (ushort)options.ReceiveWindowSize;

    private readonly TimeSpan _idleTimeout = options.IdleTimeout;
    private readonly TimeSpan _idleCheckInterval = options.IdleCheckInterval;

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
    private int _unackedSegments; // Count of in-order data segments not yet acknowledged.
    private int _ackCount;
    private int _lastZeroWinLogTick;
    private int _lastZwpLogTick;

    // Retransmission ring buffer: holds unacked bytes starting at _sndUna.
    // On loopback we don't normally need retransmission, but TUN/kernel can drop
    // packets under heavy load (no real "loss" but the effect is identical).
    // RFC 5681 fast retransmit: 3 duplicate ACKs trigger retransmit of sndUna segment.
    // PERF: capacity comes from options (RetxBufferSize, default 64 KB). Cached so the ring math
    // (% _retxCapacity) is a field read, identical cost to the former const. Bounds in-flight bytes.
    private readonly int _retxCapacity = options.RetxBufferSize;
    private readonly byte[] _retxBuffer = new byte[options.RetxBufferSize];
    private int _retxRingStart;     // index in buffer corresponding to _sndUna
    private int _retxBufferLen;     // number of valid unacked bytes
    private uint _lastDupAck;
    private int _dupAckCount;
    private long _retxCount;
    public IPEndPointPairValue IpEndPointPair => ipEndPointPair;
    public uint IsnLocal { get; } = isnLocal;
    public ushort Mss { get; } = ClampMss(peerMss, options);
    public TcpConnectionState State { get; private set; } = TcpConnectionState.SynReceived;

    /// <summary>The 16-bit TCP receive window this connection advertises on every outgoing segment.</summary>
    public ushort AdvertisedWindow => _advertisedWindow;

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
    /// Starts background tasks for this connection.
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
            clientToEnqueue = _pendingClient;
            _pendingClient = null;
        }

        if (clientToEnqueue != null && !listener.TryEnqueueAccept(clientToEnqueue)) {
            // Listener has been stopped or its accept queue is full: dispose the unaccepted
            // client (which tears down this connection via the stream's disposal).
            clientToEnqueue.Dispose();
        }
    }

    public (uint sndNxt, uint rcvNxt) SnapshotSequence()
    {
        lock (_seqLock) {
            return (_sndNxt, _rcvNxt);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { /* ignore */ }
        // Note: do NOT dispose _cts here. Background tasks may still observe the token after
        // cancellation; CTS dispose is racy with WaitForNextTickAsync. The CTS is cheap and
        // will be GC'd once tasks complete.
    }

    /// <summary>
    /// Writes data from app directly as TCP segments to the network (inline on caller's thread).
    /// Respects the peer's advertised receive window.
    /// </summary>
    public async ValueTask SendAppDataAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_disposed || _appToNetCompleted) return;
        Touch();

        var stack = _stack;
        if (stack == null) return;

        var mss = Mss;
        var offset = 0;

        while (offset < data.Length) {
            ct.ThrowIfCancellationRequested();
            if (_disposed || _appToNetCompleted) return;

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
                    var now = Environment.TickCount;
                    if (now - _lastZeroWinLogTick > 500) {
                        _lastZeroWinLogTick = now;
                        if (stack.VerboseLogging)
                            VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStackDiag, "[SEND] zero-win wait offset={Offset}/{DataLength} pw={PeerWindow} sndUna={SndUna} sndNxt={SndNxt} inFlight={InFlight}",
                                offset, data.Length, pw, una, nxt, (long)(nxt - una));
                    }
                    // Wait for peer window to open (signalled by TrySignalWindow when ACK arrives).
                    // Use a timeout so we can send a Zero Window Probe (ZWP) if the peer does not
                    // send a window update. This avoids a permanent stall when the peer (e.g. Android)
                    // expects a stimulus before it sends the window update.
                    // RFC 793: ZWP carries one byte of new data beyond the zero window.
                    using var zwpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    zwpCts.CancelAfter(options.ZeroWindowProbeInterval);
                    try { await _windowSignal.WaitAsync(zwpCts.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                        // Timeout: send a ZWP using the actual next data byte to elicit a window update.
                        // Always send regardless of in-flight data: in loopback there is no reordering,
                        // and relying on in-flight data as stimulus can stall permanently when the
                        // semaphore signal is consumed without the window actually opening.
                        if (offset < data.Length) {
                            if (Environment.TickCount - _lastZwpLogTick > 500) {
                                _lastZwpLogTick = Environment.TickCount;
                                if (stack.VerboseLogging)
                                    VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStackDiag, "[SEND] ZWP fire offset={Offset} pw={PeerWindow}", offset, pw);
                            }
                            offset += SendZeroWindowProbe(stack, data.Span[offset]);
                        }
                    }
                    catch (OperationCanceledException) { return; }
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
                lock (_seqLock) {
                    seqForSegment = _sndNxt;
                    ackForSegment = _rcvNxt;
                    _sndNxt += (uint)segLen;
                    // Append into retx ring buffer for fast retransmit support.
                    AppendToRetxBufferLocked(segmentData);
                }

                tcp.SequenceNumber = seqForSegment;
                tcp.AcknowledgmentNumber = ackForSegment;
                tcp.Acknowledgment = true;
                tcp.WindowSize = _advertisedWindow;

                // Set PSH on the last segment of the current write.
                if (offset + segLen >= data.Length)
                    tcp.Push = true;

                stack.SendPacket(packet);
                offset += segLen;
                burst -= segLen;
            }
        }
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
            Close();
            return (false, false);
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
                }
                else if (diff == 0 && payload.Length == 0 && _retxBufferLen > 0) {
                    // Pure duplicate ACK: peer is missing data starting at _sndUna.
                    if (ack == _lastDupAck) _dupAckCount++;
                    else { _lastDupAck = ack; _dupAckCount = 1; }
                    if (_dupAckCount >= 3) {
                        shouldFastRetx = true;
                        _dupAckCount = 0; // avoid retx flood; reset until next dup-ack triple
                    }
                }
                _peerWindow = (uint)windowSize << _peerWsShift;
                newPw = _peerWindow;
            }
            var ackCount = Interlocked.Increment(ref _ackCount);

            // Only log significant events (avoid log spam):
            //  - zero / very low advertised window
            //  - ACK that doesn't advance _sndUna AND has no payload (pure dup-ack / probe)
            //  - every 5000th ACK as a heartbeat
            if (windowSize == 0 || newPw < 4096 || (diff <= 0 && payload.Length == 0) || (ackCount % 5000) == 0) {
                if (_stack?.VerboseLogging == true)
                    VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStackDiag, "[ACK#{AckCount}] ack={Ack} prevUna={PrevUna} nxt={SndNxtSnap} diff={Diff} winRaw={WindowSize} pw={NewPw} payload={PayloadLength} sig={CurrentCount} dup={DupAckCount}",
                        ackCount, ack, prevUna, sndNxtSnap, diff, windowSize, newPw, payload.Length, _windowSignal.CurrentCount, _dupAckCount);
            }

            if (shouldFastRetx) {
                var n = Interlocked.Increment(ref _retxCount);
                if (_stack?.VerboseLogging == true)
                    VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStackDiag, "[RETX#{RetxCount}] fast retransmit at sndUna={Ack} retxLen={RetxBufferLen}", n, ack, _retxBufferLen);
                FastRetransmit();
            }
            TrySignalWindow();
        }

        try {
            bool needsAck;
            bool finCloses;

            lock (_seqLock) {
                var seqDiff = (long)seq - _rcvNxt;

                if (seqDiff < 0) {
                    // Retransmission: ACK without duplicating data.
                    var retransmitEnd = seq + (uint)payload.Length;
                    if (retransmitEnd > _rcvNxt && payload.Length > 0) {
                        var overlap = (int)(_rcvNxt - seq);
                        var newData = payload[overlap..];
                        if (newData.Length > 0) {
                            WriteToAppPipe(newData);
                            _rcvNxt += (uint)newData.Length;
                        }
                    }
                    return (true, true);
                }

                if (seqDiff > 0) {
                    // Out of order - send duplicate ACK to trigger fast retransmit
                    return (true, true);
                }

                // seq == _rcvNxt: in-order packet
                if (payload.Length > 0) {
                    WriteToAppPipe(payload);
                    _rcvNxt += (uint)payload.Length;
                }

                needsAck = payload.Length > 0;
                finCloses = false;

                // Delayed ACK: only ACK every 2nd in-order data segment to halve ACK traffic.
                // FIN/PSH packets bypass the delay and ACK immediately.
                if (needsAck && !flags.HasFlag(TcpFlags.Fin) && !flags.HasFlag(TcpFlags.Psh)) {
                    _unackedSegments++;
                    if (_unackedSegments < 2)
                        needsAck = false;
                    else
                        _unackedSegments = 0;
                }
                else if (needsAck) {
                    _unackedSegments = 0;
                }

                if (flags.HasFlag(TcpFlags.Fin)) {
                    _rcvNxt += 1;
                    _finReceived = true;
                    needsAck = true;

                    // Check if both sides have sent FIN
                    if (_finSent) {
                        State = TcpConnectionState.Closed;
                        finCloses = true;
                    }
                    else {
                        State = TcpConnectionState.Closing;
                    }
                }
            }

            if (flags.HasFlag(TcpFlags.Fin)) {
                CompleteNetToApp();
                if (finCloses)
                    Close();
            }

            return (true, needsAck);
        }
        catch (InvalidOperationException) {
            // Pipe was completed/broken - close connection
            Close();
            return (false, false);
        }
    }

    private void WriteToAppPipe(ReadOnlySpan<byte> data)
    {
        if (_disposed || _netToAppCompleted) return;

        var span = _netToAppPipe.Writer.GetSpan(data.Length);
        data.CopyTo(span);
        _netToAppPipe.Writer.Advance(data.Length);

        // Synchronous-style flush; we discard the ValueTask intentionally because:
        //  - On loopback, the reader (the application) drains the pipe quickly.
        //  - Awaiting here would require restructuring TryHandleIncoming as async,
        //    and we're called from the packet-receive critical path.
        //
        // KNOWN LIMITATION (memory bound is soft, not hard) — for the next maintainer:
        // We advertise a FIXED window (_advertisedWindow) on every segment and ACK data as soon
        // as it is buffered here, so PauseWriterThreshold is never actually observed (the flush
        // ValueTask is discarded). If the application stops reading, the peer keeps getting ACKs
        // and keeps sending, so this pipe can grow ~window/RTT without bound — dangerous on a
        // memory-capped host (e.g. an iOS Network Extension can be jetsam-killed).
        // Interim mitigation (shipped): a small ReceiveWindowSize + MaxConnections cap bound the
        // aggregate. Proper fix (separate, low-risk change): advertise a DYNAMIC window equal to
        // the free pipe space (ReceiveWindowSize - unread bytes), tracking app reads from
        // LocalTcpStream.ReadAsync; when the pipe fills we advertise a smaller/zero window and the
        // loopback peer stops, making ReceiveWindowSize a HARD cap. That only ever shrinks the
        // advertised window, so it stays safe on loopback.
        _ = _netToAppPipe.Writer.FlushAsync();
    }

    private void Touch()
    {
        Interlocked.Exchange(ref _lastActivityTicks, Stopwatch.GetTimestamp());
    }

    private async Task MonitorIdleAsync()
    {
        try {
            using var timer = new PeriodicTimer(_idleCheckInterval);
            while (await timer.WaitForNextTickAsync(_cts.Token)) {
                if (_disposed || State == TcpConnectionState.Closed)
                    break;

                var last = Interlocked.Read(ref _lastActivityTicks);
                var elapsed = Stopwatch.GetElapsedTime(last);
                if (elapsed >= _idleTimeout) {
                    Close();
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
        IpPacket? finPacket;
        bool closeAfter;

        lock (_seqLock) {
            if (_finSent) return;
            _finSent = true;

            _appToNetCompleted = true;

            finPacket = PacketBuilder.BuildTcp(
                IpEndPointPair.Destination, IpEndPointPair.Source,
                options: ReadOnlySpan<byte>.Empty,
                payload: ReadOnlySpan<byte>.Empty);

            var tcp = finPacket.ExtractTcp();
            tcp.SequenceNumber = _sndNxt;
            tcp.AcknowledgmentNumber = _rcvNxt;
            tcp.Finish = true;
            tcp.Acknowledgment = true;
            tcp.WindowSize = _advertisedWindow;

            _sndNxt += 1; // FIN consumes one sequence number

            closeAfter = _finReceived;
            State = closeAfter ? TcpConnectionState.Closed : TcpConnectionState.FinWait1;
        }

        stack.SendPacket(finPacket);

        if (closeAfter)
            Close();
    }

    public void TryStartFin(LocalTcpStack stack)
    {
        try { StartFin(stack); } catch { /* ignore */ }
    }

    private void CompleteNetToApp()
    {
        if (_netToAppCompleted) return;
        _netToAppCompleted = true;

        try { _netToAppPipe.Writer.Complete(); } catch { /* already completed */ }
    }

    private void TrySignalWindow()
    {
        if (_windowSignal.CurrentCount == 0) {
            try { _windowSignal.Release(); }
            catch (SemaphoreFullException) { /* already signalled */ }
        }
    }

    /// <summary>
    /// Sends a Zero Window Probe: 1 byte of actual data at sndNxt, advancing sndNxt by 1.
    /// Returns 1 if the probe was sent (so the caller can advance offset), 0 on failure.
    /// This forces the peer to ACK with its current window size, breaking the zero-window stall.
    /// </summary>
    private int SendZeroWindowProbe(LocalTcpStack stack, byte probeByte)
    {
        if (_disposed || _appToNetCompleted) return 0;

        IpPacket? probe = null;
        try {
            ReadOnlySpan<byte> probeData = [probeByte];
            probe = PacketBuilder.BuildTcp(IpEndPointPair.Destination, IpEndPointPair.Source,
                options: ReadOnlySpan<byte>.Empty, payload: probeData);
            var tcp = probe.ExtractTcp();

            uint probeSeq;
            uint probeAck;
            lock (_seqLock) {
                probeSeq = _sndNxt;
                probeAck = _rcvNxt;
                _sndNxt += 1;
            }

            tcp.SequenceNumber = probeSeq;
            tcp.AcknowledgmentNumber = probeAck;
            tcp.Acknowledgment = true;
            tcp.WindowSize = _advertisedWindow;

            stack.SendPacket(probe);
            return 1;
        }
        catch {
            probe?.Dispose();
            return 0;
        }
    }

    private void DrainWindowSignal()
    {
        while (_windowSignal.Wait(0)) { }
    }

    // _seqLock must be held.
    private void AppendToRetxBufferLocked(ReadOnlySpan<byte> segment)
    {
        if (segment.Length == 0) return;
        // Caller has already enforced (segment.Length <= _retxCapacity - _retxBufferLen) via allowable cap.
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
        lock (_seqLock) {
            if (_retxBufferLen == 0) return;
            var segLen = Math.Min(_retxBufferLen, Mss);
            payloadCopy = new byte[segLen];
            var firstChunk = Math.Min(segLen, _retxCapacity - _retxRingStart);
            Array.Copy(_retxBuffer, _retxRingStart, payloadCopy, 0, firstChunk);
            if (firstChunk < segLen)
                Array.Copy(_retxBuffer, 0, payloadCopy, firstChunk, segLen - firstChunk);
            seqForSegment = _sndUna;
            ackForSegment = _rcvNxt;
        }

        var packet = PacketBuilder.BuildTcp(IpEndPointPair.Destination, IpEndPointPair.Source,
            options: ReadOnlySpan<byte>.Empty, payload: payloadCopy);
        var tcp = packet.ExtractTcp();
        tcp.SequenceNumber = seqForSegment;
        tcp.AcknowledgmentNumber = ackForSegment;
        tcp.Acknowledgment = true;
        tcp.WindowSize = _advertisedWindow;
        tcp.Push = true;

        stack.SendPacket(packet);
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closedFlag, true))
            return;

        LocalTcpClient? abandoned;
        lock (_seqLock) {
            State = TcpConnectionState.Closed;
            _finSent = true;
            abandoned = _pendingClient;
            _pendingClient = null;
        }

        // Dispose unaccepted client if handshake never completed.
        abandoned?.Dispose();

        CompleteNetToApp();
        _appToNetCompleted = true;
        TrySignalWindow();

        try { OnClosed?.Invoke(this); } catch { /* ignore subscriber errors */ }
        Dispose();
    }
}
