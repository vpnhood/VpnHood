using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Test.Providers;

// A passthrough transport that records the UDP source port of each packet it sends, in order
public class TestPassthroughPacketTransport : PassthroughPacketTransport
{
    private readonly List<int> _sentPorts = [];

    public IReadOnlyList<int> SentPorts {
        get {
            lock (_sentPorts)
                return _sentPorts.ToArray();
        }
    }

    protected override void SendPacket(IpPacket ipPacket)
    {
        lock (_sentPorts)
            _sentPorts.Add(ipPacket.ExtractUdp().SourcePort);
    }
}
