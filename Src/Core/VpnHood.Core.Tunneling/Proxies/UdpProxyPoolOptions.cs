using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Tunneling.Proxies;

public class UdpProxyPoolOptions
{
    public required ISocketFactory SocketFactory { get; set; }
    public required IPacketProxyCallbacks? PacketProxyCallbacks { get; set; }
    public required TimeSpan UdpTimeout { get; set; } 
    public required int MaxClientCount { get; set; } 
    public required int PacketQueueCapacity { get; set; }
    public required TransferBufferSize? BufferSize { get; set; }
    public required bool AutoDisposePackets { get; set; }
    public required LogScope? LogScope { get; set; }
}