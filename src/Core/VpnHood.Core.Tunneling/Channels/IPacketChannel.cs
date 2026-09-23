using VpnHood.Net.PacketTransports;

namespace VpnHood.Core.Tunneling.Channels;

public interface IPacketChannel : IPacketTransport, IChannel
{
    int OverheadLength { get; }
}