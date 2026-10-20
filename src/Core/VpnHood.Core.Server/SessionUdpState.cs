using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Server;

// The session's UDP. Its transport is made by the session's first datagram and kept for the session's life, even when
// that datagram is forged: one per session, never one per forgery. The transport moves only on its newest authenticated
// datagram, which is also what brings its channel back; a switch to TCP keeps it, so a copy of an older datagram
// cannot take the session back to UDP
internal sealed class SessionUdpState(ulong sessionId, byte[] sessionKey, int protocolVersion, Tunnel tunnel,
    TrafficMeter trafficMeter) : IDisposable
{
    // the clock a transport counts its datagrams on from, which only grows: the wall clock at the start, in 100 ns
    // since the Unix epoch, plus a stopwatch
    private static readonly long ClockStartTicks = DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks;
    private static readonly long ClockStartTimestamp = Stopwatch.GetTimestamp();

    private readonly Lock _lock = new();
    private bool _disposed;

    // read by the listeners without the lock, on every datagram
    public SessionUdpTransport? Transport { get; private set; }

    // the tunnel's packet channel is the transport's while the transport has one
    public bool IsChannelOpen => Transport?.DataReceived != null;

    // none for a closed session, or for one below the protocol whose client gets a UDP port
    public SessionUdpTransport? GetTransport(ServerUdpChannelTransmitter transmitter)
    {
        return Transport ?? CreateTransport(transmitter);
    }

    private SessionUdpTransport? CreateTransport(ServerUdpChannelTransmitter transmitter)
    {
        if (protocolVersion < UdpChannelTransmitter.MinProtocolVersion)
            return null;

        lock (_lock) {
            if (_disposed)
                return null;

            Transport ??= new SessionUdpTransport(transmitter, sessionId, sessionKey, remoteEndPoint: null,
                isServer: true, firstSendSequenceNumber: GetClock());
            return Transport;
        }
    }

    // The transport's newest datagram found it with no channel: the tunnel takes a UDP channel in place of the TCP
    // packet channels, or of a UDP one that went. The tunnel refuses one while the session is closing
    public void OpenChannel()
    {
        lock (_lock) {
            if (_disposed || Transport is not { DataReceived: null } transport)
                return;

            tunnel.RemoveChannels<IPacketChannel>();
            var channel = new UdpChannel(transport, new UdpChannelOptions {
                Blocking = false,
                AutoDisposePackets = true,
                Lifespan = null,
                ChannelId = Guid.NewGuid().ToString(),
                TrafficMeter = trafficMeter,
                LeaveUdpTransportOpen = true
            });
            tunnel.AddChannel(channel, disposeIfFailed: true);

            VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                "UdpChannel is enabled. SessionId: {SessionId}, LocalEndPoint: {LocalEndPoint}",
                sessionId, VhLogger.Format(transport.ChannelTransmitter.LocalEndPoint));
        }
    }

    // the channel goes in and UDP out in one step: a datagram that opened UDP between the two would take the new
    // channel away and then lose its own. The tunnel disposes the UDP channel it removes; the transport stays
    public void AddTcpChannel(StreamPacketChannel channel)
    {
        lock (_lock) {
            tunnel.AddChannel(channel, disposeIfFailed: true);
            if (!IsChannelOpen)
                return;

            tunnel.RemoveChannels<UdpChannel>();
            VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                "UdpChannel is disabled. SessionId: {SessionId}", sessionId);
        }
    }

    // in nanoseconds: a session recovered after a restart keeps its key, and its new transport counts on above every seq
    // the old one sent, as long as that sent less than one datagram a nanosecond and the clock did not go back
    private static ulong GetClock()
    {
        return (ulong)(ClockStartTicks + Stopwatch.GetElapsedTime(ClockStartTimestamp).Ticks) * 100;
    }

    public void Dispose()
    {
        lock (_lock) {
            _disposed = true;
            Transport?.Dispose();
            Transport = null;
        }
    }
}
