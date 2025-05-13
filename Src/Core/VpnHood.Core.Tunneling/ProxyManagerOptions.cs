using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Tunneling;

public class ProxyManagerOptions
{
    public required TimeSpan UdpTimeout { get; init; }
    public required TimeSpan IcmpTimeout { get; init; }
    public required int MaxUdpClientCount { get; init; }
    public required int MaxPingClientCount { get; init; }
    public required int PacketQueueCapacity { get; init; }
    public required bool UseUdpProxy2 { get; init; }
    public required int? UdpSendBufferSize { get; init; }
    public required int? UdpReceiveBufferSize { get; init; }
    public required bool IsPingSupported { get; init; }
    public required IPacketProxyCallbacks? PacketProxyCallbacks { get; init; }
    public required bool AutoDisposeSentPackets { get; init; }
    public required LogScope? LogScope { get; init; }
}