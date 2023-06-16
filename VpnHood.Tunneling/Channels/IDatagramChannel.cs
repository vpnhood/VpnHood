using System;
using System.Threading.Tasks;
using PacketDotNet;

namespace VpnHood.Tunneling.Channels;

public interface IDatagramChannel : IChannel
{
    event EventHandler<ChannelPacketReceivedEventArgs> OnPacketReceived;
    Task SendPacket(IPPacket[] packets);
}