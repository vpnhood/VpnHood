using System;
using PacketDotNet;

namespace VpnHood.Tunneling;

public class ChannelPacketReceivedEventArgs : EventArgs
{
    public IPPacket[] IpPackets { get; }
    public IChannel Channel { get; }

    public ChannelPacketReceivedEventArgs(IPPacket[] ipPackets, IChannel channel)
    {
        IpPackets = ipPackets;
        Channel = channel;
    }
}