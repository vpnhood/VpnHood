using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Tunneling;

public class UdpProxyPoolOptions
{
    public required ISocketFactory SocketFactory { get; init; }
    public IPacketProxyCallbacks? PacketProxyCallbacks { get; set; }
    public TimeSpan? UdpTimeout { get; init; } = TunnelDefaults.UdpTimeout;
    public int? MaxClientCount { get; init; } = TunnelDefaults.MaxUdpClientCount;
    public int? PacketQueueCapacity { get; init; } = TunnelDefaults.ProxyPacketQueueCapacity;
    public LogScope? LogScope { get; init; }
    public int? SendBufferSize { get; init; }
    public int? ReceiveBufferSize { get; init; }
}