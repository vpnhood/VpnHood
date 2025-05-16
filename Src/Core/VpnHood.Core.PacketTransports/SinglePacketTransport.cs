using VpnHood.Core.Packets;

namespace VpnHood.Core.PacketTransports;

public abstract class SinglePacketTransport(PacketTransportOptions options)
    : PacketTransportBase(options, singleMode: false, passthrough: true)
{
    protected sealed override ValueTask SendPacketsAsync(IList<IpPacket> ipPackets)
    {
        return ipPackets.Count switch {
            0 => default,
            > 1 => throw new ArgumentOutOfRangeException(nameof(ipPackets),
                "ipPackets should not be more than 1 in SinglePacketTransport"),
            _ => SendPacketAsync(ipPackets[0])
        };
    }

    protected abstract ValueTask SendPacketAsync(IpPacket ipPacket);
}