using PacketDotNet;
using System;
using System.Collections.Generic;

namespace VpnHood.Tunneling
{
    public interface IDatagramChannel : IChannel
    {
        event EventHandler<ChannelPacketArrivalEventArgs> OnPacketReceived;
        void SendPackets(IEnumerable<IPPacket> packets);
    }
}
