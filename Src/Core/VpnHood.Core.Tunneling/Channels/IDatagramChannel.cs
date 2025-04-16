using PacketDotNet;
using VpnHood.Core.Packets;

namespace VpnHood.Core.Tunneling.Channels;

public interface IDatagramChannel : IChannel
{
    event EventHandler<PacketReceivedEventArgs> PacketReceived;
    bool IsStream { get; }

    // it is not thread-safe
    Task SendPacketAsync(IList<IPPacket> packets);

    // it is not thread-safe
    Task SendPacketAsync(IPPacket packet);
}