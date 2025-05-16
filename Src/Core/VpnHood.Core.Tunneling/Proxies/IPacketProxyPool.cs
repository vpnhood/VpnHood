using VpnHood.Core.Packets.Transports;

namespace VpnHood.Core.Tunneling.Proxies;

public interface IPacketProxyPool : IPacketTransport
{
    public int ClientCount { get; }
    public int RemoteEndPointCount { get; }
}