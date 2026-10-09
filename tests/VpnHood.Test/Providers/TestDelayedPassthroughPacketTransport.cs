using VpnHood.Net.Packets;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Test.Providers;

// A passthrough transport whose send does not complete at once, as a passthrough send must
public class TestDelayedPassthroughPacketTransport(TimeSpan delay)
    : PacketTransportBase(new PacketTransportOptions { AutoDisposePackets = true, Blocking = false },
        singleMode: true, passthrough: true)
{
    public volatile bool IsSendFinished;

    protected override async ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        await Task.Delay(delay);
        IsSendFinished = true;
    }
}
