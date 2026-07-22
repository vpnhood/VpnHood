using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.TcpStack.Abstractions;
using VpnHood.Core.TcpStack.Primitives;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Memory;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.TcpStack;

/// <summary>
/// A lightweight, localhost-only TCP stack implementation designed for VpnHood's TunVpnAdapter.
/// This stack is optimized for local loopback connections where packet loss is not expected.
/// </summary>
/// <remarks>
/// IMPORTANT: <see cref="OnPacketSend"/> hands ownership of the produced <see cref="IpPacket"/>
/// to the consumer. The consumer is responsible for disposing it (the WinDivert adapter does
/// this automatically when AutoDisposePackets = true). When no consumer is registered, the
/// stack disposes the packet itself to release pooled memory.
/// </remarks>
public sealed class LocalTcpStack : ITcpStack
{
    // Validated memory/timeout knobs for this stack and the connections it spawns.
    private readonly LocalTcpStackOptions _options;
    // Built once from _options and shared (immutably) by every connection's reassembly pipe.
    private readonly PipeOptions _pipeOptions;

    /// <summary>Gets the diagnostic metrics for this stack instance.</summary>
    public TcpStackDiagnostics Diagnostics { get; } = new();

    /// <summary>Exposes the active stack's diagnostics globally for external processes (such as the iOS memory probe).</summary>
    public static TcpStackDiagnostics? ActiveDiagnostics { get; private set; }

    /// <summary>
    /// Creates a TCP stack. When <paramref name="options"/> is null the footprint is chosen
    /// automatically for the current platform via <see cref="LocalTcpStackOptions.ForCurrentPlatform"/>
    /// (small on iOS/tvOS Network Extensions, full-size everywhere else) — so callers such as
    /// ClientTcpHost don't need to know the platform and Android throughput is unaffected.
    /// Pass an explicit <see cref="LocalTcpStackOptions"/> to override.
    /// </summary>
    public LocalTcpStack(LocalTcpStackOptions? options = null)
    {
        _options = (options ?? LocalTcpStackOptions.ForCurrentPlatform()).Validated();
        _pipeOptions = _options.CreatePipeOptions();
        Diagnostics.ConfiguredReceiveWindow = _options.ReceiveWindowSize;   // DIAGNOSTIC: confirm active profile
        Diagnostics.ConfiguredMaxConnections = _options.MaxConnections;
        ActiveDiagnostics = Diagnostics;

        // Window re-advertisement sweep. A flow throttled to a (near-)zero receive window under the
        // shared GlobalReceiveBudget can have its window reopen when OTHER flows drain the budget;
        // with its own app stalled at that zero window, no OnAppConsumed drain fires to deliver the
        // update, so the peer freezes until its slow zero-window persist timer (the "upload freezes
        // then resumes" stall). This timer re-advertises such reopened windows promptly. Only armed
        // when the budget is bounded (iOS): with the default unbounded budget (Android/desktop)
        // cross-flow starvation cannot occur, so that path stays completely unchanged.
        if (_options.GlobalReceiveBudget != long.MaxValue)
            _windowSweepTimer = new Timer(_ => SweepReopenedWindows(), null,
                WindowSweepInterval, WindowSweepInterval);

        // Maintenance sweep: SYN-ACK retransmission, coarse RTO for unacked data/FIN (the only recovery
        // for tail loss — see LocalTcpConnection.PollMaintenance) and delayed-ACK flushing. Created
        // parked; armed only while at least one connection has registered interest, so a fully idle
        // stack causes NO periodic wake-ups (battery matters on mobile).
        _maintenanceTimer = new Timer(_ => SweepMaintenance(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Gets or sets a value indicating whether verbose diagnostic information should be logged via VhLogger.
    /// This is useful for debugging TCP connection issues (e.g., retransmissions, zero-window probes).
    /// Backed by <see cref="TcpStackDiagnostics.VerboseLogging"/> so the diagnoser owns all trace gating.
    /// </summary>
    public bool VerboseLogging {
        get => Diagnostics.VerboseLogging;
        set => Diagnostics.VerboseLogging = value;
    }

    private readonly ConcurrentDictionary<IPEndPointPairValue, LocalTcpConnection> _connections = new();
    private readonly ConcurrentDictionary<IpEndPointValue, LocalTcpListener> _listeners = new();
    private readonly Lock _anyListenerLock = new();
    private LocalTcpListener? _anyListener;
    private bool _disposed;

    // PERF: kept in lockstep with _connections membership so the SYN-path cap check and the
    // diagnostics never call ConcurrentDictionary.Count (which locks every stripe).
    private int _connectionCount;

    // Re-advertise reopened-but-quiet receive windows this often. 50 ms caps the added stall to well
    // under a peer's zero-window persist RTO (~hundreds of ms, then exponential), so the freeze is
    // bounded to one sweep instead. Per tick, it only flag-checks each connection; lock/send work
    // happens solely for flows actually waiting on a reopened window.
    private static readonly TimeSpan WindowSweepInterval = TimeSpan.FromMilliseconds(50);
    private readonly Timer? _windowSweepTimer;
    private int _sweeping; // reentrancy guard so a slow sweep never overlaps itself

    // Maintenance sweep cadence. Retransmit timing granularity is this interval; RetransmitTimeout
    // (default 500 ms) stays a comfortable multiple of it.
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMilliseconds(100);
    private readonly Timer _maintenanceTimer;
    private int _maintenanceInterest; // # connections currently needing maintenance service
    private int _maintenanceSweeping; // reentrancy guard so a slow sweep never overlaps itself

    /// <summary>
    /// Callback invoked when a TCP packet needs to be sent out. The callback takes ownership of the packet.
    /// </summary>
    public Action<IpPacket>? OnPacketSend { get; set; }

    /// <summary>
    /// Creates a TCP listener on the specified local endpoint.
    /// </summary>
    public LocalTcpListener Listen(IpEndPointValue localEndPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _listeners.GetOrAdd(localEndPoint, ep => new LocalTcpListener(this, ep, _options.AcceptQueueCapacity));
    }

    /// <summary>
    /// Creates a TCP listener that accepts connections on any endpoint (IPv4 and IPv6).
    /// </summary>
    public LocalTcpListener ListenAny()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_anyListenerLock) {
            return _anyListener ??= new LocalTcpListener(this, null, _options.AcceptQueueCapacity);
        }
    }

    ITcpListener ITcpStack.ListenAny() => ListenAny();
    ITcpListener ITcpStack.Listen(IPEndPoint localEndPoint) => Listen(localEndPoint.ToValue());

    public bool StopListening(IPEndPoint localEndPoint)
    {
        return StopListening(localEndPoint.ToValue());
    }

    /// <summary>
    /// Stops listening on the specified endpoint and removes the listener.
    /// </summary>
    public bool StopListening(IpEndPointValue localEndPoint)
    {
        if (!_listeners.TryRemove(localEndPoint, out var listener))
            return false;

        listener.Stop();
        return true;
    }

    /// <summary>
    /// Stops the wildcard listener that accepts connections on any endpoint.
    /// </summary>
    internal bool StopListeningAny()
    {
        lock (_anyListenerLock) {
            if (_anyListener == null)
                return false;

            _anyListener = null;
            return true;
        }
    }

    /// <summary>
    /// Processes an already-parsed incoming IP packet without taking ownership.
    /// The caller remains responsible for disposing <paramref name="ipPacket"/>.
    /// </summary>
    public void ProcessIncoming(IpPacket ipPacket)
    {
        if (_disposed)
            return;

        try {
            ProcessIncomingInternal(ipPacket);
        }
        catch (Exception ex) {
            // Never let a malformed/unexpected packet disrupt the receive loop. Surface it only under
            // verbose logging so a genuine bug isn't hidden behind a blind catch.
            if (VerboseLogging)
                VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStack, ex,
                    "TcpStack dropped a packet it could not process.");
        }
    }

    private void ProcessIncomingInternal(IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.Tcp)
            return;

        var tcpPacket = ipPacket.ExtractTcp();
        var endPointPair = new IPEndPointPairValue(
            new IpEndPointValue(ipPacket.SourceAddress, tcpPacket.SourcePort),
            new IpEndPointValue(ipPacket.DestinationAddress, tcpPacket.DestinationPort));

        // Handle SYN packets (new connection requests)
        if (tcpPacket is { Synchronize: true, Acknowledgment: false }) {
            HandleSynPacket(endPointPair, tcpPacket);
            return;
        }

        // Handle packets for existing connections
        if (_connections.TryGetValue(endPointPair, out var existing)) {
            HandleExistingConnection(existing, tcpPacket);
            return;
        }

        // Unknown connection - send RST if not already an RST
        if (!tcpPacket.Reset)
            SendRst(endPointPair.Destination, endPointPair.Source, tcpPacket);
    }

    private void HandleSynPacket(IPEndPointPairValue ipEndPointPair, TcpPacket tcpPacket)
    {
        var listener = ResolveListener(ipEndPointPair.Destination);
        if (listener == null) {
            // No listener - send RST
            SendRst(ipEndPointPair.Destination, ipEndPointPair.Source, tcpPacket);
            return;
        }

        // SYN for a 4-tuple we already track.
        if (_connections.TryGetValue(ipEndPointPair, out var existingConn)) {
            if (existingConn.State == TcpConnectionState.SynReceived)
                // SYN retransmit before our SYN-ACK was acknowledged: re-send SYN-ACK.
                SendSynAck(existingConn);
            else
                // SYN on an already-established/closing connection (RFC 9293/5961): the peer most likely
                // abandoned the old connection and reused the 4-tuple. Send a challenge ACK; if the old
                // connection is truly dead on the peer's side it replies with RST, which closes our stale
                // entry and lets the peer's SYN-retransmit open a fresh one. Previously this SYN was
                // silently dropped, black-holing the new connection until the idle timeout.
                SendAckOnly(existingConn);
            return;
        }

        // Memory admission gate: while the process footprint is critical, DROP the SYN silently — no
        // RST — so the peer's own SYN-retransmit paces new-flow establishment until memory recedes (see
        // LocalTcpStackOptions.AdmissionMemoryLimitMb). Checked BEFORE the connection cap so pressure
        // never triggers admit-by-evicting churn. null (default) short-circuits: desktop/Android unchanged.
        if (_options.AdmissionMemoryLimitMb is { } admissionLimitMb) {
            var footprintMb = VhMemory.Instance.GetInfo().ProcessFootprintMb;
            if (footprintMb >= admissionLimitMb) {
                Diagnostics.OnAdmissionDeferred(ipEndPointPair, footprintMb.Value);
                return;
            }
        }

        // Admission control: cap concurrent connections to bound aggregate buffer memory on
        // constrained platforms (e.g. iOS). 0 = unbounded (default) and short-circuits with zero
        // overhead, so the Android/desktop SYN path is unchanged. When full, try to evict the most
        // idle ("most unused") connection to make room (EvictionMinIdle); if none qualifies, fall
        // back to rejecting the new SYN with an RST — the historical behavior.
        if (_options.MaxConnections > 0 && Volatile.Read(ref _connectionCount) >= _options.MaxConnections &&
            !TryEvictForAdmission()) {
            SendRst(ipEndPointPair.Destination, ipEndPointPair.Source, tcpPacket);
            return;
        }

        var isnLocal = (uint)RandomNumberGenerator.GetInt32(int.MaxValue);
        var peerMss = ParseMssOption(tcpPacket.Options.Span);
        var peerWsShift = ParseWindowScaleOption(tcpPacket.Options.Span);
        var connection = new LocalTcpConnection(
            ipEndPointPair, isnLocal, tcpPacket.SequenceNumber, peerMss, listener, peerWsShift, _options, _pipeOptions, Diagnostics);
        VpnHood.Core.Toolkit.Memory.VhTypeTracker.Track(connection);
        connection.OnClosed += OnConnectionClosed;

        if (!_connections.TryAdd(ipEndPointPair, connection)) {
            connection.Dispose();
            return;
        }

        // Guard the check-then-act race with Dispose(): if the stack was disposed while we were adding,
        // its dispose loop may have already drained _connections — remove and tear down immediately so
        // the connection (and its idle-monitor task) cannot outlive the stack for the full IdleTimeout.
        if (_disposed) {
            if (_connections.TryRemove(ipEndPointPair, out _))
                connection.Dispose();
            return;
        }

        // DIAGNOSTIC: track concurrent-connection count + high-water mark.
        Diagnostics.SetConnectionCount(Interlocked.Increment(ref _connectionCount));

        try {
            // Start() before the SYN-ACK so the connection is fully wired (stack reference, maintenance
            // interest for SYN-ACK retransmission) by the time the peer can answer. The listener still
            // gets the stream only after the final ACK arrives (MarkEstablished).
            connection.Start(this);
            SendSynAck(connection);
        }
        catch {
            // A failed SYN-ACK send must not leave a zombie in _connections with no client and no
            // pending accept — it would linger until the idle timeout while holding a connection slot.
            if (_connections.TryRemove(ipEndPointPair, out _))
                Diagnostics.SetConnectionCount(Interlocked.Decrement(ref _connectionCount));
            connection.Dispose();
            throw;
        }
    }

    internal void SendSynAck(LocalTcpConnection conn)
    {
        // Advertise our MSS (default 1460) so the peer doesn't fall back to the default 536-byte MSS.
        // Without this option, the peer will send us 536-byte packets which dramatically
        // increases packet count (≈2.7x) and slows down the receiving path.
        // Format: kind=2, len=4, value=MSS (16-bit big-endian).
        // Window Scale option (kind=3, len=3, shift=0): enables WS negotiation so the peer
        // can advertise windows larger than 64 KB in its ACKs. Our own shift stays 0 (we never
        // advertise a window > 65535 — ReceiveWindowSize is validated to fit the 16-bit field).
        var advertisedMss = _options.MaxMss;
        // MSS(4) + NOP(1) + WS(3) = 8 bytes, 4-byte aligned.
        ReadOnlySpan<byte> options = [2, 4, (byte)(advertisedMss >> 8), (byte)(advertisedMss & 0xFF), 1, 3, 3, 0];
        var packet = PacketBuilder.BuildTcp(
            conn.IpEndPointPair.Destination, conn.IpEndPointPair.Source,
            options: options,
            payload: ReadOnlySpan<byte>.Empty);

        var tcp = packet.ExtractTcp();
        // Idempotent across SYN retransmits: SndNxt must be ISN+1 once SYN-ACK is sent.
        conn.SetSndNxtAfterSyn();
        var (_, rcvNxt, window) = conn.SnapshotForAck();
        tcp.SequenceNumber = conn.IsnLocal;
        tcp.AcknowledgmentNumber = rcvNxt;
        tcp.Synchronize = true;
        tcp.Acknowledgment = true;
        tcp.WindowSize = window;

        SendPacket(packet);
    }

    private void HandleExistingConnection(LocalTcpConnection conn, TcpPacket tcpPacket)
    {
        var flags = (TcpFlags)0;
        if (tcpPacket.Finish) flags |= TcpFlags.Fin;
        if (tcpPacket.Reset) flags |= TcpFlags.Rst;
        if (tcpPacket.Acknowledgment) flags |= TcpFlags.Ack;
        if (tcpPacket.Push) flags |= TcpFlags.Psh;

        // Transition from SynReceived to Established only after the ACK proves it
        // acknowledges our SYN-ACK and continues from the peer SYN.
        if (conn.State == TcpConnectionState.SynReceived && !tcpPacket.Reset) {
            if (!conn.IsValidHandshakeAck(tcpPacket.SequenceNumber, tcpPacket.AcknowledgmentNumber, flags)) {
                SendSynAck(conn);
                return;
            }

            conn.MarkEstablished();
        }

        var (handled, needsAck) = conn.TryHandleIncoming(
            tcpPacket.SequenceNumber,
            tcpPacket.AcknowledgmentNumber,
            tcpPacket.WindowSize,
            flags,
            tcpPacket.Payload.Span);

        if (!handled || !needsAck)
            return;

        SendAckOnly(conn);
    }

    internal void SendAckOnly(LocalTcpConnection conn)
    {
        var packet = PacketBuilder.BuildTcp(
            conn.IpEndPointPair.Destination, conn.IpEndPointPair.Source,
            options: ReadOnlySpan<byte>.Empty,
            payload: ReadOnlySpan<byte>.Empty);

        var tcp = packet.ExtractTcp();
        // Single-lock snapshot: sequence numbers and the advertised window come from one atomic view.
        var (sndNxt, rcvNxt, window) = conn.SnapshotForAck();
        tcp.SequenceNumber = sndNxt;
        tcp.AcknowledgmentNumber = rcvNxt;
        tcp.Acknowledgment = true;
        tcp.WindowSize = window;

        SendPacket(packet);
    }

    private void SendRst(IpEndPointValue localEndPoint, IpEndPointValue remoteEndPoint, TcpPacket incomingTcp)
    {
        var rstPacket = PacketBuilder.BuildTcp(
            localEndPoint, remoteEndPoint,
            options: ReadOnlySpan<byte>.Empty,
            payload: ReadOnlySpan<byte>.Empty);

        var rstTcp = rstPacket.ExtractTcp();
        rstTcp.Reset = true;

        // RFC 793: If ACK bit is off, seq = 0, ack = seq + segment length
        // If ACK bit is on, seq = ack number from incoming
        if (incomingTcp.Acknowledgment) {
            rstTcp.SequenceNumber = incomingTcp.AcknowledgmentNumber;
        }
        else {
            rstTcp.SequenceNumber = 0;
            var ackNum = incomingTcp.SequenceNumber + (uint)incomingTcp.Payload.Length;
            if (incomingTcp.Synchronize) ackNum += 1;
            if (incomingTcp.Finish) ackNum += 1;
            rstTcp.AcknowledgmentNumber = ackNum;
            rstTcp.Acknowledgment = true;
        }

        SendPacket(rstPacket);
    }

    private void OnConnectionClosed(LocalTcpConnection conn)
    {
        VpnHood.Core.Toolkit.Memory.VhTypeTracker.Record("LocalTcpConnection.closed");
        if (_connections.TryRemove(conn.IpEndPointPair, out _))
            Diagnostics.SetConnectionCount(Interlocked.Decrement(ref _connectionCount));
    }

    /// <summary>Registers a connection with the maintenance sweep. Arms the parked timer on the
    /// 0 → 1 transition; the sweep parks itself again once interest returns to zero.</summary>
    internal void AddMaintenanceInterest()
    {
        if (Interlocked.Increment(ref _maintenanceInterest) == 1 && !_disposed)
            try { _maintenanceTimer.Change(MaintenanceInterval, MaintenanceInterval); }
            catch (ObjectDisposedException) { /* stack disposed concurrently */ }
    }

    /// <summary>Unregisters a connection from the maintenance sweep. The timer is parked by the sweep
    /// itself once it observes zero interest — disarming here would race a concurrent
    /// <see cref="AddMaintenanceInterest"/> and could leave a live connection unserviced.</summary>
    internal void RemoveMaintenanceInterest()
    {
        Interlocked.Decrement(ref _maintenanceInterest);
    }

    /// <summary>
    /// Timer callback: services every connection that needs retransmission (SYN-ACK / data / FIN on
    /// ACK silence) or a delayed-ACK flush (see <see cref="LocalTcpConnection.PollMaintenance"/>).
    /// Never throws — an unhandled exception in a Timer callback terminates the whole process.
    /// </summary>
    private void SweepMaintenance()
    {
        if (_disposed)
            return;

        // Skip if a previous sweep is still running (defensive; a sweep is normally microseconds).
        if (Interlocked.Exchange(ref _maintenanceSweeping, 1) != 0)
            return;

        try {
            // PERF: enumerate the dictionary directly — the enumerator is lock-free and allocation-free,
            // whereas .Values takes every stripe lock and copies all values into a snapshot List on each
            // 100 ms tick. A connection added mid-sweep is simply serviced on the next tick.
            foreach (var kvp in _connections)
                if (kvp.Value.HasMaintenanceInterest)
                    kvp.Value.PollMaintenance();
        }
        catch (Exception ex) {
            if (VerboseLogging)
                VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStack, ex, "Maintenance sweep failed.");
        }
        finally {
            Volatile.Write(ref _maintenanceSweeping, 0);
        }

        // Park the timer when nothing needs service. Re-check afterward: an AddMaintenanceInterest
        // racing the park may have had its Change() overwritten — re-arm so its connection is not
        // left unserviced.
        if (Volatile.Read(ref _maintenanceInterest) == 0)
            try {
                _maintenanceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                if (Volatile.Read(ref _maintenanceInterest) > 0)
                    _maintenanceTimer.Change(MaintenanceInterval, MaintenanceInterval);
            }
            catch (ObjectDisposedException) { /* stack disposed concurrently */ }
    }

    /// <summary>
    /// Timer callback: re-advertises any flow whose receive window closed under flow control and has
    /// since reopened (see <see cref="LocalTcpConnection.PollWindowReopen"/>). Never throws — an
    /// unhandled exception in a Timer callback terminates the whole process.
    /// </summary>
    private void SweepReopenedWindows()
    {
        if (_disposed)
            return;

        // Skip if a previous sweep is still running (defensive; a sweep is normally microseconds).
        if (Interlocked.Exchange(ref _sweeping, 1) != 0)
            return;

        try {
            // PERF: lock-free enumerator — see SweepMaintenance for why .Values is avoided in sweeps.
            foreach (var kvp in _connections)
                kvp.Value.PollWindowReopen();
        }
        catch (Exception ex) {
            if (VerboseLogging)
                VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStack, ex, "Window-reopen sweep failed.");
        }
        finally {
            Volatile.Write(ref _sweeping, 0);
        }
    }

    /// <summary>
    /// At the <see cref="LocalTcpStackOptions.MaxConnections"/> cap, closes the single most idle
    /// ("most unused") connection to free a slot for a new one — but only if its idle time meets
    /// <see cref="LocalTcpStackOptions.EvictionMinIdle"/>, so actively-transferring flows are never
    /// killed. Closing the victim propagates EOF to its consumer (the proxy's QUIC stream / TCP
    /// channel), tearing down the whole upstream flow immediately. Returns true if a slot was freed.
    /// </summary>
    private bool TryEvictForAdmission()
    {
        if (_options.EvictionMinIdle == TimeSpan.MaxValue)
            return false; // eviction disabled → caller falls back to RST (historical behavior)

        var victim = FindMostIdleEvictable(_options.EvictionMinIdle);
        if (victim == null)
            return false; // every connection is too recently active → reject the newcomer

        VhLogger.Instance.LogWarning(TcpStackEventIds.TcpStack,
            "[TcpStack] Killing idle connection {EndPointPair} (idle={IdleSeconds:F1}s) to admit a new one: " +
            "the connection cap ({MaxConnections}) is reached. This is forced by the iOS Network Extension " +
            "memory restriction.",
            victim.IpEndPointPair, victim.IdleDuration.TotalSeconds, _options.MaxConnections);

        // Abort() is synchronous: it removes the victim from _connections (via OnConnectionClosed),
        // completes its pipes (consumer reads EOF), unblocks its sender — freeing the slot now — and
        // tells the victim's peer with an RST so it does not keep retransmitting into a black hole.
        victim.Abort();
        return true;
    }

    /// <summary>
    /// Returns the single most idle ("most unused") connection whose idle time is at least
    /// <paramref name="minIdle"/>, or <c>null</c> when none qualifies (every connection is more
    /// recently active than the threshold).
    /// </summary>
    private LocalTcpConnection? FindMostIdleEvictable(TimeSpan minIdle)
    {
        LocalTcpConnection? best = null;
        var bestIdle = minIdle; // a candidate must beat the threshold first, then each other
        // PERF: lock-free enumerator — see SweepMaintenance for why .Values is avoided in scans.
        foreach (var kvp in _connections) {
            var conn = kvp.Value;
            if (conn.IsClosed) continue;
            var idle = conn.IdleDuration;
            if (idle < bestIdle) continue;
            best = conn;
            bestIdle = idle;
        }
        return best;
    }

    /// <summary>
    /// Closes all currently active connections. New connections can still be accepted afterward.
    /// </summary>
    public void DropAllConnections()
    {
        foreach (var kvp in _connections) {
            if (_connections.TryRemove(kvp.Key, out var connection)) {
                Diagnostics.SetConnectionCount(Interlocked.Decrement(ref _connectionCount));
                connection.Dispose();
            }
        }
    }

    /// <summary>
    /// Updates checksums and hands the packet to the consumer. If no consumer is registered the
    /// pooled buffer is released so we never leak memory.
    /// </summary>
    internal void SendPacket(IpPacket packet)
    {
        var callback = OnPacketSend;
        if (callback == null) {
            packet.Dispose();
            return;
        }

        try {
            packet.UpdateAllChecksums();
            callback(packet);
        }
        catch {
            // If the consumer threw, dispose to avoid leaking the pooled buffer.
            try { packet.Dispose(); } catch { /* ignore */ }
            throw;
        }
    }

    private LocalTcpListener? ResolveListener(IpEndPointValue endPoint)
    {
        // Specific listener wins over wildcard
        if (_listeners.TryGetValue(endPoint, out var listener))
            return listener;

        return _anyListener;
    }

    /// <summary>
    /// Parses the TCP "Maximum Segment Size" option (kind=2, len=4) from the SYN options.
    /// Returns null when the option is absent or malformed.
    /// </summary>
    private static ushort? ParseMssOption(ReadOnlySpan<byte> options)
    {
        var i = 0;
        while (i < options.Length) {
            var kind = options[i];
            switch (kind) {
                case 0: return null;
                case 1: i++; continue;
            }
            if (i + 1 >= options.Length) return null;
            var len = options[i + 1];
            if (len < 2 || i + len > options.Length) return null;
            if (kind == 2 && len == 4)
                return (ushort)((options[i + 2] << 8) | options[i + 3]);
            i += len;
        }
        return null;
    }

    /// <summary>
    /// Parses the TCP "Window Scale" option (kind=3, len=3) from the SYN options.
    /// Returns 0 when the option is absent or malformed (no scaling).
    /// </summary>
    private static byte ParseWindowScaleOption(ReadOnlySpan<byte> options)
    {
        var i = 0;
        while (i < options.Length) {
            var kind = options[i];
            switch (kind) {
                case 0: return 0; // End of option list
                case 1: i++; continue; // NOP
            }
            if (i + 1 >= options.Length) return 0;
            var len = options[i + 1];
            if (len < 2 || i + len > options.Length) return 0;
            if (kind == 3 && len == 3) // Window Scale
                return options[i + 2];
            i += len;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _windowSweepTimer?.Dispose();
        _maintenanceTimer.Dispose();

        if (ActiveDiagnostics == Diagnostics)
            ActiveDiagnostics = null;

        foreach (var kvp in _listeners) {
            if (_listeners.TryRemove(kvp.Key, out var listener))
                listener.Dispose();
        }

        LocalTcpListener? anyListener;
        lock (_anyListenerLock) {
            anyListener = _anyListener;
            _anyListener = null;
        }
        anyListener?.Dispose();

        foreach (var kvp in _connections) {
            if (_connections.TryRemove(kvp.Key, out var connection)) {
                Diagnostics.SetConnectionCount(Interlocked.Decrement(ref _connectionCount));
                connection.Dispose();
            }
        }
    }
}
