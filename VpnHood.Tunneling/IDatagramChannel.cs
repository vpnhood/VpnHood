using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PacketDotNet;

namespace VpnHood.Tunneling
{
    public interface IDatagramChannel : IChannel
    {
        event EventHandler<ChannelPacketReceivedEventArgs> OnPacketReceived;
        Task SendPacketAsync(IEnumerable<IPPacket> packets);
    }
}