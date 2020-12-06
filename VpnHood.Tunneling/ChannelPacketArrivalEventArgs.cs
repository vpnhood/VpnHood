using PacketDotNet;
using System;

namespace VpnHood.Tunneling
{
    public class ChannelPacketArrivalEventArgs : EventArgs
    {
        public IPPacket IpPacket { get; }
        public IChannel Channel { get; }

        public ChannelPacketArrivalEventArgs(IPPacket ipPacket, IChannel channel)
        {
            IpPacket = ipPacket;
            Channel = channel;
        }
    }

}
