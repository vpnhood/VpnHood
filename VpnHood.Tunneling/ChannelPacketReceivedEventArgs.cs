using System;
using System.Collections.Generic;
using PacketDotNet;

namespace VpnHood.Tunneling
{
    public class ChannelPacketReceivedEventArgs : EventArgs
    {
        public ChannelPacketReceivedEventArgs(IEnumerable<IPPacket> ipPackets, IChannel channel)
        {
            IpPackets = ipPackets;
            Channel = channel;
        }

        public IEnumerable<IPPacket> IpPackets { get; }
        public IChannel Channel { get; }
    }
}