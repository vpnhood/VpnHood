using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.TcpStack.Abstractions;
using VpnHood.Net.TcpStack.Primitives;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Memory;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.TcpStack;

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
    public TcpStackDiagnostics Diagnostics { get; }

    /// <summary>The unread bytes in every connection's reassembly pipe, summed on read: nothing is counted per packet.</summary>
    internal long PipeBufferedBytes {
        get {
            long total = 0;
            foreach (var kvp in _connections)
                total += kvp.Value.PipeUnread;
            return total;
        }
    }

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
        Diagnostics = new TcpStackDiagnostics(this);
        _options = (options ?? LocalTcpStackOptions.ForCurrentPlatform()).Validated();
        _pipeOptions = _options.CreatePipeOptions();
        Diagnostics.ConfiguredReceiveWindow = _options.ReceiveWindowSize;   // DIAGNOSTIC: confirm active profile
        Diagnostics.ConfiguredMaxConnections = _options.MaxConnections;
        ActiveDiagnostics = Diagnostics;

        // Maintenance sweep: SYN-ACK retransmission, coarse RTO for unacked data/FIN (the only recovery
        // for tail loss — see LocalTcpConnection.PollMaintenance) and delayed-ACK flushing. Created
        // parked; armed only while at least one connection has registered interest, so a fully idle
        // stack causes NO periodic wake-ups (battery matters on mobile).
        _maintenanceTimer = new Timer(_ => SweepMaintenance(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        // Idle sweep: one timer at IdleCheckInterval over every connection (LocalTcpConnection.PollIdle), parked
        // while no connection exists, like the maintenance sweep.
        _idleTimer = new Timer(_ => SweepIdle(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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

    private readonly ConcurrentDictionary<ConnectionKey, LocalTcpConnection> _connections = new();
    private readonly ConcurrentDictionary<IpEndPointValue, LocalTcpListener> _listeners = new();
    private readonly Lock _anyListenerLock = new();
    private LocalTcpListener? _anyListener;
    private bool _disposed;

    // Maintenance sweep cadence. Retransmit timing granularity is this interval; RetransmitTimeout
    // (default 500 ms) stays a comfortable multiple of it.
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMilliseconds(100);
    private readonly Timer _maintenanceTimer;
    private int _maintenanceInterest; // # connections currently needing maintenance service
    private int _maintenanceSweeping; // reentrancy guard so a slow sweep never overlaps itself
    private readonly Timer _idleTimer;
    private int _idleSweeping;

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
        var listener = _listeners.GetOrAdd(localEndPoint, ep => new LocalTcpListener(this, ep, _options.AcceptQueueCapacity));

        // A Dispose that drained the table before this add would leave the listener open for good: stopped here instead,
        // the check-then-act guard HandleSynPacket has for connections.
        if (!_disposed)
            return listener;

        listener.Stop();
        throw new ObjectDisposedException(GetType().FullName);
    }

    /// <summary>
    /// Creates a TCP listener that accepts connections on any endpoint (IPv4 and IPv6).
    /// </summary>
    public LocalTcpListener ListenAny()
    {
        lock (_anyListenerLock) {
            // under the lock Dispose takes after it sets _disposed: a call it overtook throws instead of publishing a
            // listener the disposed stack would never close
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _anyListener ??= new LocalTcpListener(this, null, _options.AcceptQueueCapacity);
        }
    }

    ITcpListener ITcpStack.ListenAny() => ListenAny();
    ITcpListener ITcpStack.Listen(IPEndPoint localEndPoint) => Listen(localEndPoint.ToValue());

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
    /// Takes a stopping listener out of its slot, only if it still holds it: a replacement registered on its endpoint
    /// after StopListening took it out stays.
    /// </summary>
    internal void RemoveListener(LocalTcpListener listener)
    {
        if (listener.LocalEndPoint is { } localEndPoint) {
            _listeners.TryRemove(new KeyValuePair<IpEndPointValue, LocalTcpListener>(localEndPoint, listener));
            return;
        }

        lock (_anyListenerLock) {
            if (_anyListener == listener)
                _anyListener = null;
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
            // the packet's processing ends here and the receive loop goes on; this line is the only one: the
            // transport's dispatch logs a handler's exception at Trace only
            Diagnostics.LogFailure(nameof(ProcessIncoming), ex);
        }
    }

    private void ProcessIncomingInternal(IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.Tcp)
            return;

        var tcpPacket = ipPacket.ExtractTcp();
        // The table's key, read from the packet's bytes: a segment of a known connection costs no IPAddress; the SYN
        // and the RST below build their endpoints.
        var key = ConnectionKey.From(ipPacket, tcpPacket);

        // A tracked tuple's segment, a SYN too: its connection answers it
        if (_connections.TryGetValue(key, out var existing)) {
            HandleExistingConnection(existing, tcpPacket);
            return;
        }

        // A SYN alone opens a connection. With ACK it answers an open we never made, and with RST it is dropped (RFC 9293
        // 3.10.7.2): both take the unknown connection's path below.
        if (tcpPacket is { Synchronize: true, Acknowledgment: false, Reset: false }) {
            HandleSynPacket(key, GetEndPointPair(ipPacket, tcpPacket), tcpPacket);
            return;
        }

        // Unknown connection - send RST if not already an RST
        if (!tcpPacket.Reset) {
            var endPointPair = GetEndPointPair(ipPacket, tcpPacket);
            SendRst(endPointPair.Destination, endPointPair.Source, tcpPacket);
        }
    }

    private static IPEndPointPairValue GetEndPointPair(IpPacket ipPacket, TcpPacket tcpPacket) => new(
        new IpEndPointValue(ipPacket.SourceAddress, tcpPacket.SourcePort),
        new IpEndPointValue(ipPacket.DestinationAddress, tcpPacket.DestinationPort));

    private void HandleSynPacket(ConnectionKey key, IPEndPointPairValue ipEndPointPair, TcpPacket tcpPacket)
    {
        var listener = ResolveListener(ipEndPointPair.Destination);
        if (listener == null) {
            // No listener - send RST
            SendRst(ipEndPointPair.Destination, ipEndPointPair.Source, tcpPacket);
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
        // back to rejecting the new SYN with an RST. The count lives in the diagnostics (never
        // ConcurrentDictionary.Count, which locks every stripe) and is taken before the table holds the
        // connection, so whoever removes the entry always has a count to give back.
        if (!TryTakeConnectionSlot(out var connectionCount)) {
            SendRst(ipEndPointPair.Destination, ipEndPointPair.Source, tcpPacket);
            return;
        }

        var connection = CreateConnection(key, ipEndPointPair, tcpPacket, listener);
        var entry = new KeyValuePair<ConnectionKey, LocalTcpConnection>(key, connection);
        if (!_connections.TryAdd(key, connection)) {
            Diagnostics.AddConnections(-1);
            connection.Dispose();
            return;
        }

        // Guard the check-then-act race with Dispose(): if the stack was disposed while we were adding, its dispose
        // loop may have already drained _connections and taken the count; otherwise remove and tear down here, so the
        // connection cannot outlive the stack for the full IdleTimeout.
        if (_disposed) {
            if (_connections.TryRemove(entry))
                Diagnostics.AddConnections(-1);
            connection.Dispose();
            return;
        }

        // The first connection arms the idle sweep.
        if (connectionCount == 1)
            _idleTimer.Change(_options.IdleCheckInterval, _options.IdleCheckInterval);

        try {
            SendSynAck(connection);
        }
        catch {
            // A failed SYN-ACK send must not leave a zombie in _connections with no client and no
            // pending accept — it would linger until the idle timeout while holding a connection slot.
            if (_connections.TryRemove(entry))
                Diagnostics.AddConnections(-1);
            connection.Dispose();
            throw;
        }
    }

    // Takes the new connection's slot. Under a cap the test and the add are one atomic step, so SYNs racing at the cap
    // cannot all pass it; a full table evicts its most idle connection and tries once more.
    private bool TryTakeConnectionSlot(out int connectionCount)
    {
        if (_options.MaxConnections <= 0) {
            connectionCount = Diagnostics.AddConnections(1);
            return true;
        }

        return Diagnostics.TryReserveConnection(_options.MaxConnections, out connectionCount) ||
               TryEvictForAdmission() && Diagnostics.TryReserveConnection(_options.MaxConnections, out connectionCount);
    }

    // The SYN's connection, wired (the stack reference, the stream, the maintenance interest for the SYN-ACK's
    // retransmission) before the table publishes it to the sweeps, DropAllConnections and the receive path; the
    // listener still gets the stream only after the final ACK arrives (MarkEstablished). Its slot is taken already: a
    // throw here gives it back, or the count would stay one high for the stack's life.
    private LocalTcpConnection CreateConnection(ConnectionKey key, IPEndPointPairValue ipEndPointPair, TcpPacket tcpPacket,
        LocalTcpListener listener)
    {
        try {
            Span<byte> isnBytes = stackalloc byte[4];
            RandomNumberGenerator.Fill(isnBytes); // all 32 bits random (RFC 6528)
            var isnLocal = BinaryPrimitives.ReadUInt32LittleEndian(isnBytes);
            var peerMss = ParseMssOption(tcpPacket.Options.Span);
            var peerWsShift = ParseWindowScaleOption(tcpPacket.Options.Span);
            var connection = new LocalTcpConnection(key, ipEndPointPair, isnLocal, tcpPacket.SequenceNumber, peerMss,
                listener, peerWsShift, _options, _pipeOptions, Diagnostics);
            VhTypeTracker.Track(connection);
            connection.OnClosed += OnConnectionClosed;
            connection.Start(this);
            return connection;
        }
        catch {
            Diagnostics.AddConnections(-1);
            throw;
        }
    }

    internal void SendSynAck(LocalTcpConnection conn)
    {
        // Advertise our MSS (default 1460) so the peer doesn't fall back to the default 536-byte MSS.
        // Without this option, the peer will send us 536-byte packets which dramatically
        // increases packet count (≈2.7x) and slows down the receiving path.
        // Format: kind=2, len=4, value=MSS (16-bit big-endian).
        // Window Scale option (kind=3, len=3, shift=0): completes the WS negotiation so the peer
        // can advertise windows larger than 64 KB in its ACKs; only in answer to the SYN's own (RFC 7323
        // 2.2), as without it neither side scales. Our own shift stays 0 (we never
        // advertise a window > 65535 — ReceiveWindowSize is validated to fit the 16-bit field).
        var advertisedMss = _options.MaxMss;
        // MSS(4) + NOP(1) + WS(3) = 8 bytes, or the MSS alone; both 4-byte aligned.
        ReadOnlySpan<byte> options = [2, 4, (byte)(advertisedMss >> 8), (byte)(advertisedMss & 0xFF), 1, 3, 3, 0];
        var packet = PacketBuilder.BuildTcp(
            conn.IpEndPointPair.Destination, conn.IpEndPointPair.Source,
            options: conn.PeerOfferedWindowScale ? options : options[..4],
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
        // One read of the flags byte: TcpFlags are its wire bits, and URG, ECE and CWR are not the stack's. Bit tests
        // below, not Enum.HasFlag, which boxes in the JIT's first-tier code.
        var flags = (TcpFlags)tcpPacket.Flags & (TcpFlags.Fin | TcpFlags.Syn | TcpFlags.Rst | TcpFlags.Psh | TcpFlags.Ack);

        // A SYN on a tracked tuple, checked after the RST (RFC 9293 3.10.7.4), so a SYN with RST is the RST's. A bare SYN
        // in SynReceived is the peer's retransmission before our SYN-ACK arrived: the SYN-ACK again. Any other, on an
        // established or closing connection or with ACK (an answer to an open we never made), draws a challenge ACK
        // (RFC 5961 4) and is dropped with its data: a peer that abandoned the old connection answers it with a RST,
        // which closes our stale entry and lets its SYN retransmission open a fresh one.
        if ((flags & TcpFlags.Syn) != 0 && (flags & TcpFlags.Rst) == 0) {
            if (conn.State == TcpConnectionState.SynReceived && (flags & TcpFlags.Ack) == 0)
                SendSynAck(conn);
            else
                SendAckOnly(conn);
            return;
        }

        // SynReceived: the ACK must acknowledge our SYN-ACK (see IsValidHandshakeAck); the data path
        // below takes the segment from there. The right ack beyond the window gets a plain ACK (RFC 9293
        // 3.10.7.4); a wrong ack, a stale or forged segment, keeps the old tolerance: a SYN-ACK.
        if (conn.State == TcpConnectionState.SynReceived && (flags & TcpFlags.Rst) == 0) {
            if (!conn.IsValidHandshakeAck(tcpPacket.SequenceNumber, tcpPacket.AcknowledgmentNumber, flags)) {
                if (conn.AcknowledgesOurSyn(tcpPacket.AcknowledgmentNumber))
                    SendAckOnly(conn);
                else
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
        // by key and value: a late close of a connection already replaced on its tuple must not remove the successor
        if (_connections.TryRemove(new KeyValuePair<ConnectionKey, LocalTcpConnection>(conn.Key, conn)))
            Diagnostics.AddConnections(-1);
    }

    /// <summary>Registers a connection with the maintenance sweep. Arms the parked timer on the
    /// 0 → 1 transition; the sweep parks itself again once interest returns to zero.</summary>
    internal void AddMaintenanceInterest()
    {
        // Timer.Change after Dispose returns false and throws nothing (so in every Change below).
        if (Interlocked.Increment(ref _maintenanceInterest) == 1 && !_disposed)
            _maintenanceTimer.Change(MaintenanceInterval, MaintenanceInterval);
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
            Diagnostics.LogFailure(nameof(SweepMaintenance), ex);
        }
        finally {
            Volatile.Write(ref _maintenanceSweeping, 0);
        }

        // Park the timer when nothing needs service. Re-check afterward: an AddMaintenanceInterest
        // racing the park may have had its Change() overwritten — re-arm so its connection is not
        // left unserviced.
        if (Volatile.Read(ref _maintenanceInterest) == 0) {
            _maintenanceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (Volatile.Read(ref _maintenanceInterest) > 0)
                _maintenanceTimer.Change(MaintenanceInterval, MaintenanceInterval);
        }
    }

    /// <summary>
    /// Timer callback: every IdleCheckInterval each connection samples its orphan progress and aborts itself once
    /// idle for IdleTimeout (see <see cref="LocalTcpConnection.PollIdle"/>). Never throws — an unhandled exception
    /// in a Timer callback terminates the whole process.
    /// </summary>
    private void SweepIdle()
    {
        if (_disposed)
            return;

        if (Interlocked.Exchange(ref _idleSweeping, 1) != 0)
            return;

        try {
            foreach (var kvp in _connections)
                kvp.Value.PollIdle();
        }
        catch (Exception ex) {
            Diagnostics.LogFailure(nameof(SweepIdle), ex);
        }
        finally {
            Volatile.Write(ref _idleSweeping, 0);
        }

        // Park the timer when no connection is left; re-check afterward, as a SYN racing the park may have had
        // its Change() overwritten.
        if (Diagnostics.ConnectionCount == 0) {
            _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (Diagnostics.ConnectionCount > 0)
                _idleTimer.Change(_options.IdleCheckInterval, _options.IdleCheckInterval);
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
            return false; // eviction disabled: the caller rejects with a RST

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
        // Abort, not Dispose: the local app gets a RST instead of a black-holed socket, the stream's reader
        // EOF; OnConnectionClosed removes the entry and keeps the count.
        foreach (var kvp in _connections)
            kvp.Value.Abort();
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
    /// Returns null when the option is absent or malformed: neither side scales, and the SYN-ACK carries none.
    /// </summary>
    private static byte? ParseWindowScaleOption(ReadOnlySpan<byte> options)
    {
        var i = 0;
        while (i < options.Length) {
            var kind = options[i];
            switch (kind) {
                case 0: return null; // End of option list
                case 1: i++; continue; // NOP
            }
            if (i + 1 >= options.Length) return null;
            var len = options[i + 1];
            if (len < 2 || i + len > options.Length) return null;
            if (kind == 3 && len == 3) // Window Scale
                return options[i + 2];
            i += len;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _maintenanceTimer.Dispose();
        _idleTimer.Dispose();

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
                Diagnostics.AddConnections(-1);
                connection.Dispose();
            }
        }
    }
}
