using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Tunneling;

public class ProxyManagerOptions
{
    public TimeSpan? UdpTimeout { get; init; }
    public TimeSpan? IcmpTimeout { get; init; }
    public int? MaxUdpClientCount { get; init; }
    public int? MaxIcmpClientCount { get; init; }
    public int? PacketQueueCapacity { get; init; }
    public bool UseUdpProxy2 { get; init; }
    public int? UdpSendBufferSize { get; init; }
    public int? UdpReceiveBufferSize { get; init; }
    public LogScope? LogScope { get; init; }
    public bool IsPingSupported { get; init; }
    public IPacketProxyCallbacks? PacketProxyCallbacks { get; init; }
}