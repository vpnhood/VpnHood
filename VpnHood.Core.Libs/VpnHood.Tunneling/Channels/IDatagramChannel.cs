using PacketDotNet;

namespace VpnHood.Tunneling.Channels;

public interface IDatagramChannel : IChannel
{
    event EventHandler<ChannelPacketReceivedEventArgs> PacketReceived;
    Task SendPacket(IList<IPPacket> packets);
    bool IsStream { get; }
}