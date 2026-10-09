using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Test.Providers;

// A passthrough transport that records the UDP source port of each packet it sends, in order
public class TestPassthroughPacketTransport : PassthroughPacketTransport
{
    private readonly List<int> _sentPorts = [];

    // set, a packet from this UDP source port is refused before it is handed on
    public volatile int FailPort;

    public IReadOnlyList<int> SentPorts {
        get {
            lock (_sentPorts)
                return _sentPorts.ToArray();
        }
    }

    protected override void SendPacket(IpPacket ipPacket)
    {
        var sourcePort = ipPacket.ExtractUdp().SourcePort;
        if (sourcePort == FailPort)
            throw new InvalidOperationException("Test: no one takes this packet.");

        lock (_sentPorts)
            _sentPorts.Add(sourcePort);
    }
}
