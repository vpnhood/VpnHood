using VpnHood.Core.Packets;

namespace VpnHood.Core.PacketTransports;

public abstract class PassthroughPacketTransport(bool autoDisposePackets)
    : PacketTransportBase(new PacketTransportOptions {
        AutoDisposePackets = autoDisposePackets,
        Blocking = false
    }, passthrough: true, singleMode: true)
{
    protected sealed override ValueTask SendPacketsAsync(IList<IpPacket> ipPackets)
    {
        switch (ipPackets.Count) {
            case 0:
                return default;
            case > 1:
                throw new ArgumentOutOfRangeException(nameof(ipPackets),
                    "ipPackets should not be more than 1 in SinglePacketTransport");
            default:
                SendPacket(ipPackets[0]);
                return default;
        }
    }

    protected abstract void SendPacket(IpPacket ipPacket);

}
