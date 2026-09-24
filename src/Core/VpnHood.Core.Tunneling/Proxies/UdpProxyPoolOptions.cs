using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Proxies;

public class UdpProxyPoolOptions
{
    public required ISocketFactory SocketFactory { get; set; }
    public required IPacketProxyCallbacks? PacketProxyCallbacks { get; set; }
    public required TimeSpan UdpTimeout { get; set; }
    public required int MaxClientCount { get; set; }
    // defaulted (not required) so existing callers keep the core default
    public int MaxDnsClientCount { get; set; } = TransportDefaults.MaxUdpDnsClientCount;
    public required int PacketQueueCapacity { get; set; }
    public required TransferBufferSize? BufferSize { get; set; }
    public required bool AutoDisposePackets { get; set; }
    public required LogScope? LogScope { get; set; }
}