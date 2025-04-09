using PacketDotNet;

namespace VpnHood.Core.Packets;

public sealed class PacketReceivedEventArgs(IList<IPPacket> ipPackets) : EventArgs
{
    public IList<IPPacket> IpPackets { get; set; } = ipPackets;
}