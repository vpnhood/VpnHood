using PacketDotNet;
using VpnHood.Core.Packets;

namespace VpnHood.Core.Tunneling.Channels;

public interface IDatagramChannel : IChannel
{
    event EventHandler<PacketReceivedEventArgs> PacketReceived;
    void SendPacket(IList<IPPacket> packets);
    void SendPacket(IPPacket packet);
    Task SendPacketAsync(IList<IPPacket> packets);
    Task SendPacketAsync(IPPacket packet);
    bool IsStream { get; }
}