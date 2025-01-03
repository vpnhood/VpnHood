﻿using PacketDotNet;

namespace VpnHood.Core.Tunneling.Channels;

public class ChannelPacketReceivedEventArgs(IPPacket[] ipPackets, IChannel channel) : EventArgs
{
    public IPPacket[] IpPackets { get; } = ipPackets;
    public IChannel Channel { get; } = channel;
}