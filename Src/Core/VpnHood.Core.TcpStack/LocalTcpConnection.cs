using System.Diagnostics;
using System.IO.Pipelines;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.TcpStack.Primitives;

namespace VpnHood.Core.TcpStack;

internal sealed class LocalTcpConnection(
    IpEndPointQuad endPointQuad,
    uint isnLocal,
    uint isnRemote,
    ushort? peerMss,
    LocalTcpListener listener,
    byte peerWsShift = 0,
    TimeSpan? tcpTimeout = null)
    : IDisposable
{
    // Diagnostic logging hook (set from app for tests). Receives free-form lines.
    public static Action<string>? DiagLog;
    private static void Log(string msg) { try { DiagLog?.Invoke(msg); } catch { /* ignore */ } }

    // Static TCP receive window advertised to the peer (no window scaling).
    private const ushort LoopbackWindowSize = 0xFFFF;

    // Conservative fallback when peer SYN does not advertise an MSS.
    private const ushort DefaultMss = 536;

    // Standard Ethernet MSS cap. Peer's advertised MSS is used when smaller.
    private const ushort MaxMss = 1460;

    private readonly TimeSpan _idleTimeout = tcpTimeout ?? TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromMinutes(1);

    // Pipe options - for network -> app data only (stream reads).
    private static readonly PipeOptions PipeOpts = new(
        pauseWriterThreshold: LoopbackWindowSize,
        resumeWriterThreshold: LoopbackWindowSize / 2,
        useSynchronizationContext: false);

    // Pipe for network -> app data (stream reads)
    private readonly Pipe _netToAppPipe = new(PipeOpts);

    private readonly Lock _seqLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _windowSignal = new(0, 1);
    private LocalTcpClient? _pendingClient;
    private LocalTcpStack? _stack;

    private bool _finSent;
    private bool _finReceived;
    private bool _sndNxtAfterSynSet;
    private bool _disposed;
    private int _closedFlag;
    private long _lastActivityTicks = Stopwatch.GetTimestamp();
    private bool _netToAppCompleted;
    private bool _appToNetCompleted;
    private uint _sndNxt = isnLocal; // SYN sequence; bumped to ISN+1 after SYN-ACK is sent.
    private uint _sndUna = isnLocal; // Oldest unacknowledged byte.
    private readonly byte _peerWsShift = peerWsShift > 14 ? (byte)14 : peerWsShift; // Peer's window scale shift (RFC 1323).
    private uint _peerWindow = LoopbackWindowSize; // Peer's last advertised receive window (scaled).
    private uint _rcvNxt = isnRemote + 1; // We have already "consumed" the peer's SYN.
    private int _unackedSegments; // Count of in-order data segments not yet acknowledged.
    private int _ackCount;
    private int _lastZeroWinLogTick;
    private int _lastZwpLogTick;

    // Retransmission ring buffer: holds unacked bytes starting at _sndUna.
    // On loopback we don't normally need retransmission, but TUN/kernel can drop
    // packets under heavy load (no real "loss" but the effect is identical).
    // RFC 5681 fast retransmit: 3 duplicate ACKs trigger retransmit of sndUna segment.
    private const int RetxBufferSize = 64 * 1024; // bound in-flight unacked bytes
    private readonly byte[] _retxBuffer = new byte[RetxBufferSize];
    private int _retxRingStart;     // index in buffer corresponding to _sndUna
    private int _retxBufferLen;     // number of valid unacked bytes
    private uint _lastDupAck;
    private int _dupAckCount;
    private long _retxCount;
    public IpEndPointQuad EndPointQuad => endPointQuad;
    public uint IsnLocal { get; } = isnLocal;
    public ushort Mss { get; } = ClampMss(peerMss);
    public TcpConnectionState State { get; private set; } = TcpConnectionState.SynReceived;

    /// <summary>
    /// PipeReader for reading data received from network (used by LocalTcpStream)
    /// </summary>
    public PipeReader NetToAppReader => _netToAppPipe.Reader;

    /// <summary>
    /// Event raised when connection is fully closed and should be removed from the stack.
    /// </summary>
    public event Action<LocalTcpConnection>? OnClosed;

    private static ushort ClampMss(ushort? peerMss)
    {
        if (peerMss is null or 0) return DefaultMss;
        var v = peerMss.Value;
        if (v < 64) return 64;          // pathological lower bound
        if (v > MaxMss) return MaxMss;
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
            endPointQuad.Destination.ToIPEndPoint(),
            endPointQuad.Source.ToIPEndPoint());

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
            // Listener has been stopped: dispose client and reset the connection
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
                int retxFree;
                lock (_seqLock) {
                    pw = _peerWindow; una = _sndUna; nxt = _sndNxt;
                    var inFlight = (long)(nxt - una);
                    var fromPeer = (int)Math.Max(0, _peerWindow - inFlight);
                    retxFree = RetxBufferSize - _retxBufferLen;
                    allowable = Math.Min(fromPeer, retxFree);
                }

                if (allowable <= 0) {
                    var now = Environment.TickCount;
                    if (now - _lastZeroWinLogTick > 500) {
                        _lastZeroWinLogTick = now;
                        Log($"[SEND] zero-win wait offset={offset}/{data.Length} pw={pw} sndUna={una} sndNxt={nxt} inFlight={(long)(nxt - una)}");
                    }
                    // Wait for peer window to open (signalled by TrySignalWindow when ACK arrives).
                    // Use a timeout so we can send a Zero Window Probe (ZWP) if the peer does not
                    // send a window update. This avoids a permanent stall when the peer (e.g. Android)
                    // expects a stimulus before it sends the window update.
                    // RFC 793: ZWP carries one byte of new data beyond the zero window.
                    using var zwpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    zwpCts.CancelAfter(TimeSpan.FromMilliseconds(200));
                    try { await _windowSignal.WaitAsync(zwpCts.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                        // Timeout: send a ZWP using the actual next data byte to elicit a window update.
                        // Always send regardless of in-flight data: in loopback there is no reordering,
                        // and relying on in-flight data as stimulus can stall permanently when the
                        // semaphore signal is consumed without the window actually opening.
                        if (offset < data.Length) {
                            if (Environment.TickCount - _lastZwpLogTick > 500) {
                                _lastZwpLogTick = Environment.TickCount;
                                Log($"[SEND] ZWP fire offset={offset} pw={pw}");
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

                var packet = PacketBuilder.BuildTcp(EndPointQuad.Destination, EndPointQuad.Source,
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
                tcp.WindowSize = LoopbackWindowSize;

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
                diff = (long)(ack - _sndUna);
                if (diff > 0 && diff <= (long)(_sndNxt - _sndUna)) {
                    _sndUna = ack;
                    // Drop acknowledged bytes from retx buffer.
                    var n = (int)diff;
                    if (n >= _retxBufferLen) {
                        _retxBufferLen = 0;
                        _retxRingStart = 0;
                    }
                    else {
                        _retxRingStart = (_retxRingStart + n) % RetxBufferSize;
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
            if (windowSize == 0 || newPw < 4096 || (diff <= 0 && payload.Length == 0) || (ackCount % 5000) == 0)
                //Log($"[ACK#{ackCount}] ack={ack} prevUna={prevUna} nxt={sndNxtSnap} diff={diff} winRaw={windowSize} pw={newPw} payload={payload.Length} sig={_windowSignal.CurrentCount} dup={_dupAckCount}");
                if (shouldFastRetx) {
                    var n = Interlocked.Increment(ref _retxCount);
                    //Log($"[RETX#{n}] fast retransmit at sndUna={ack} retxLen={_retxBufferLen}");
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
        // PauseWriterThreshold still bounds memory because every Pipe segment respects it
        // when GetSpan/Advance are paired with FlushAsync; for very large bursts the writer
        // is observably "busy" via UnflushedBytes which we can revisit if needed.
        _ = _netToAppPipe.Writer.FlushAsync();
    }

    private void Touch()
    {
        Interlocked.Exchange(ref _lastActivityTicks, Stopwatch.GetTimestamp());
    }

    private async Task MonitorIdleAsync()
    {
        try {
            using var timer = new PeriodicTimer(IdleCheckInterval);
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
                EndPointQuad.Destination, EndPointQuad.Source,
                options: ReadOnlySpan<byte>.Empty,
                payload: ReadOnlySpan<byte>.Empty);

            var tcp = finPacket.ExtractTcp();
            tcp.SequenceNumber = _sndNxt;
            tcp.AcknowledgmentNumber = _rcvNxt;
            tcp.Finish = true;
            tcp.Acknowledgment = true;
            tcp.WindowSize = LoopbackWindowSize;

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
            probe = PacketBuilder.BuildTcp(EndPointQuad.Destination, EndPointQuad.Source,
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
            tcp.WindowSize = LoopbackWindowSize;

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
        // Caller has already enforced (segment.Length <= RetxBufferSize - _retxBufferLen) via allowable cap.
        var writeIdx = (_retxRingStart + _retxBufferLen) % RetxBufferSize;
        var firstChunk = Math.Min(segment.Length, RetxBufferSize - writeIdx);
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
            var firstChunk = Math.Min(segLen, RetxBufferSize - _retxRingStart);
            Array.Copy(_retxBuffer, _retxRingStart, payloadCopy, 0, firstChunk);
            if (firstChunk < segLen)
                Array.Copy(_retxBuffer, 0, payloadCopy, firstChunk, segLen - firstChunk);
            seqForSegment = _sndUna;
            ackForSegment = _rcvNxt;
        }

        var packet = PacketBuilder.BuildTcp(EndPointQuad.Destination, EndPointQuad.Source,
            options: ReadOnlySpan<byte>.Empty, payload: payloadCopy);
        var tcp = packet.ExtractTcp();
        tcp.SequenceNumber = seqForSegment;
        tcp.AcknowledgmentNumber = ackForSegment;
        tcp.Acknowledgment = true;
        tcp.WindowSize = LoopbackWindowSize;
        tcp.Push = true;

        stack.SendPacket(packet);
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closedFlag, 1) != 0)
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
