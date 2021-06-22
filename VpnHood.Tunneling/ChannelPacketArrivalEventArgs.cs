using PacketDotNet;
using System;
using System.Collections.Generic;

namespace VpnHood.Tunneling
{
    public class ChannelPacketArrivalEventArgs : EventArgs
    {
        public IEnumerable<IPPacket> IpPackets { get; }
        public IChannel Channel { get; }

        public ChannelPacketArrivalEventArgs(IEnumerable<IPPacket> ipPackets, IChannel channel)
        {
            IpPackets = ipPackets;
            Channel = channel;
        }
    }

}
