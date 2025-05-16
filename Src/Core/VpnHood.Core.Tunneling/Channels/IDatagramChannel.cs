using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;

namespace VpnHood.Core.Tunneling.Channels;

public interface IDatagramChannel : IChannel
{
    event EventHandler<PacketReceivedEventArgs> PacketReceived;
    bool IsStream { get; }

    // it is not thread-safe
    Task SendPacketAsync(IList<IpPacket> packets);

    // it is not thread-safe
    Task SendPacketAsync(IpPacket packet);
}