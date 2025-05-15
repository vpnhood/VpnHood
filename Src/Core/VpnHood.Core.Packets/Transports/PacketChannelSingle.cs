namespace VpnHood.Core.Packets.Transports;

public abstract class PacketChannelSingle(PacketChannelOptions options)
    : PacketChannel(options, singleMode: true)
{
    protected sealed override ValueTask SendPacketsAsync(List<IpPacket> ipPackets)
    {
        return ipPackets.Count switch {
            0 => default,
            > 1 => throw new ArgumentOutOfRangeException(nameof(ipPackets),
                "ipPackets should not be more than 1 in PacketChannelSingle"),
            _ => SendPacketAsync(ipPackets[0])
        };
    }

    protected abstract ValueTask SendPacketAsync(IpPacket ipPacket);
}