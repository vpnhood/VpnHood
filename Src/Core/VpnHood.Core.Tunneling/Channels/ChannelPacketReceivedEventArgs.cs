using PacketDotNet;

namespace VpnHood.Core.Tunneling.Channels;

public class ChannelPacketReceivedEventArgs(IList<IPPacket> ipPackets, IChannel channel) : EventArgs
{
    public IList<IPPacket> IpPackets { get; set; } = ipPackets;
    public IChannel Channel { get; } = channel;
}