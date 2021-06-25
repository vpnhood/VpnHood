using PacketDotNet;
using System;
using System.Collections.Generic;

namespace VpnHood.Tunneling
{
    public class ChannelPacketReceivedEventArgs : EventArgs
    {
        public IEnumerable<IPPacket> IpPackets { get; }
        public IChannel Channel { get; }

        public ChannelPacketReceivedEventArgs(IEnumerable<IPPacket> ipPackets, IChannel channel)
        {
            IpPackets = ipPackets;
            Channel = channel;
        }
    }

}
