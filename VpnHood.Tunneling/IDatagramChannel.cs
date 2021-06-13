using PacketDotNet;
using System;

namespace VpnHood.Tunneling
{
    public interface IDatagramChannel : IChannel
    {
        event EventHandler<ChannelPacketArrivalEventArgs> OnPacketReceived;
        void SendPackets(IPPacket[] packets);
    }
}
