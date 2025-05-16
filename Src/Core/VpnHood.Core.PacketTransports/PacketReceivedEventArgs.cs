using VpnHood.Core.Packets;

namespace VpnHood.Core.PacketTransports;

// todo: remove
public sealed class PacketReceivedEventArgs(IList<IpPacket> ipPackets) : EventArgs
{
    public IList<IpPacket> IpPackets { get; set; } = ipPackets;
}