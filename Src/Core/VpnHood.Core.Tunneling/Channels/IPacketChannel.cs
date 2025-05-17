using VpnHood.Core.PacketTransports;

namespace VpnHood.Core.Tunneling.Channels;

public interface IPacketChannel : IChannel, IPacketTransport
{
    bool IsStream { get; }
}