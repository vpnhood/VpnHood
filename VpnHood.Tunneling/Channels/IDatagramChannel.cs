using PacketDotNet;

namespace VpnHood.Tunneling.Channels;

public interface IDatagramChannel : IChannel
{
    event EventHandler<ChannelPacketReceivedEventArgs> OnPacketReceived;
    Task SendPacket(IPPacket[] packets);
    bool IsStream { get; }
}