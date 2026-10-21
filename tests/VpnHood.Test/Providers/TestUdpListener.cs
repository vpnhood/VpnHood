using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Server;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Test.Providers;

// a server's UDP listener with no session manager: it hands each datagram to its session's channel, as the listener
// host does. A session another listener added stays that one's to dispose
internal sealed class TestUdpListener : IUdpDatagramHandler, IDisposable
{
    private readonly ConcurrentDictionary<ulong, ServerUdpPacketChannel> _channels = new();
    private readonly List<ServerUdpPacketChannel> _ownChannels = [];

    public UdpChannelTransmitter Transmitter { get; }
    public IPEndPoint LocalEndPoint => Transmitter.LocalEndPoint;

    public TestUdpListener()
    {
        Transmitter = new UdpChannelTransmitter(new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)), this);
    }

    public ServerUdpPacketChannel AddSession(ulong sessionId, byte[] sessionKey, bool isLegacy = false)
    {
        var channel = new ServerUdpPacketChannel(sessionId, sessionKey, new TrafficMeter(), isLegacy);
        _channels[sessionId] = channel;
        _ownChannels.Add(channel);
        return channel;
    }

    // another listener's session, as a server's listeners all reach its sessions
    public void AddForeignSession(ServerUdpPacketChannel channel)
    {
        _channels[channel.SessionId] = channel;
    }

    public void OnDatagramReceived(UdpChannelTransmitter transmitter, Memory<byte> datagram, IPEndPoint remoteEndPoint)
    {
        if (!_channels.TryGetValue(UdpDatagram.ReadSessionId(datagram.Span), out var channel))
            return;

        if (channel.Receive(transmitter, datagram, remoteEndPoint, out _) != DatagramFreshness.Seen)
            channel.Deliver(datagram);
    }

    public void Dispose()
    {
        Transmitter.Dispose();
        foreach (var channel in _ownChannels)
            channel.Dispose();
    }
}
