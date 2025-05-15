namespace VpnHood.Core.Packets.Transports;

// todo: remove
public sealed class PacketReceivedEventArgs(IList<IpPacket> ipPackets) : EventArgs
{
    public IList<IpPacket> IpPackets { get; set; } = ipPackets;
}