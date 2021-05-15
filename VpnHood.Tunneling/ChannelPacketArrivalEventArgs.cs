using PacketDotNet;
using System;

namespace VpnHood.Tunneling
{
    public class ChannelPacketArrivalEventArgs : EventArgs
    {
        public IPPacket[] IpPackets { get; }
        public IChannel Channel { get; }

        public ChannelPacketArrivalEventArgs(IPPacket[] ipPackets, IChannel channel)
        {
            IpPackets = ipPackets;
            Channel = channel;
        }
    }

}
