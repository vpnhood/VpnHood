using VpnHood.Core.Packets.VhPackets;

namespace VpnHood.Core.Packets;

public sealed class PacketReceivedEventArgs(IList<IpPacket> ipPackets) : EventArgs
{
    public IList<IpPacket> IpPackets { get; set; } = ipPackets;
}