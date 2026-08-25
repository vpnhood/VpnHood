using VpnHood.Core.Toolkit.Net;

namespace VpnHood.AppLib;

// Pure passthrough to ClientOptions; the app never reads these, it only forwards them at connect.
// Per-platform tuning lives here (iOS lowers the buffers for the ~50 MB Network Extension jetsam limit).
public class AppTransportConfig
{
    public required TimeSpan SessionTimeout { get; init; }
    public required TimeSpan UnstableTimeout { get; init; }
    public required TimeSpan AutoWaitTimeout { get; init; }
    public required TimeSpan TcpConnectTimeout { get; init; }
    public required TimeSpan ServerQueryTimeout { get; init; }
    public required TransferBufferSize? PacketChannelBufferSize { get; init; }
    public required TransferBufferSize? UdpProxyBufferSize { get; init; }
    public required TransferBufferSize? StreamProxyBufferSize { get; init; }
    public required TransferBufferSize? TcpKernelBufferSize { get; init; }
    public required TransferBufferSize? TcpPacketChannelKernelBufferSize { get; init; }
    public required int? MaxUdpClientCount { get; init; }
    public required int? MaxUdpDnsClientCount { get; init; }
    public required int? UdpProxyQueueCapacity { get; init; }
}
