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
        return _listeners.TryRemove(localEndPoint, out _);
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
                VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStackDiag, ex,
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

        // Admission control: cap concurrent connections to bound aggregate buffer memory on
        // constrained platforms (e.g. iOS). 0 = unbounded (default) and short-circuits with zero
        // overhead, so the Android/desktop SYN path is unchanged.
        if (_options.MaxConnections > 0 && _connections.Count >= _options.MaxConnections) {
            SendRst(ipEndPointPair.Destination, ipEndPointPair.Source, tcpPacket);
            return;
        }

        var isnLocal = (uint)RandomNumberGenerator.GetInt32(int.MaxValue);
        var peerMss = ParseMssOption(tcpPacket.Options.Span);
        var peerWsShift = ParseWindowScaleOption(tcpPacket.Options.Span);
        var connection = new LocalTcpConnection(
            ipEndPointPair, isnLocal, tcpPacket.SequenceNumber, peerMss, listener, peerWsShift, _options, _pipeOptions, Diagnostics);
        connection.OnClosed += OnConnectionClosed;

        if (!_connections.TryAdd(ipEndPointPair, connection)) {
            connection.Dispose();
            return;
        }

        // DIAGNOSTIC: track concurrent-connection count + high-water mark.
        Diagnostics.SetConnectionCount(_connections.Count);

        SendSynAck(connection);

        // Note: do NOT enqueue accept yet. The listener gets the stream only after the
        // final ACK arrives and the connection transitions to Established.
        connection.Start(this);
    }

    private void SendSynAck(LocalTcpConnection conn)
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
        var (_, rcvNxt) = conn.SnapshotSequence();
        tcp.SequenceNumber = conn.IsnLocal;
        tcp.AcknowledgmentNumber = rcvNxt;
        tcp.Synchronize = true;
        tcp.Acknowledgment = true;
        tcp.WindowSize = conn.UpdateAdvertisedWindow();

        SendPacket(packet);
    }

    private void HandleExistingConnection(LocalTcpConnection conn, TcpPacket tcpPacket)
    {
        // Transition from SynReceived to Established on first valid ACK
        if (conn.State == TcpConnectionState.SynReceived && tcpPacket.Acknowledgment)
            conn.MarkEstablished();

        var flags = (TcpFlags)0;
        if (tcpPacket.Finish) flags |= TcpFlags.Fin;
        if (tcpPacket.Reset) flags |= TcpFlags.Rst;
        if (tcpPacket.Acknowledgment) flags |= TcpFlags.Ack;
        if (tcpPacket.Push) flags |= TcpFlags.Psh;

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
        var (sndNxt, rcvNxt) = conn.SnapshotSequence();
        tcp.SequenceNumber = sndNxt;
        tcp.AcknowledgmentNumber = rcvNxt;
        tcp.Acknowledgment = true;
        tcp.WindowSize = conn.UpdateAdvertisedWindow();

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
        _connections.TryRemove(conn.IpEndPointPair, out _);
        Diagnostics.SetConnectionCount(_connections.Count);
    }

    /// <summary>
    /// Closes all currently active connections. New connections can still be accepted afterward.
    /// </summary>
    public void DropAllConnections()
    {
        foreach (var kvp in _connections) {
            if (_connections.TryRemove(kvp.Key, out var connection))
                connection.Dispose();
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
            if (_connections.TryRemove(kvp.Key, out var connection))
                connection.Dispose();
        }
    }
}
