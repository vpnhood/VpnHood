using VpnHood.Core.Packets.Transports;

namespace VpnHood.Core.Tunneling.Proxies;

public interface IPacketProxyPool : IPacketChannel
{
    public int ClientCount { get; }
    public int RemoteEndPointCount { get; }
}