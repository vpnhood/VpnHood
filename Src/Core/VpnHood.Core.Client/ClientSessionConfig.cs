using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Client;

internal class ClientSessionConfig
{
    public required TimeSpan TcpConnectTimeout { get; init; }
    public required TransferBufferSize? UdpProxyBufferSize { get; init; }
    public required TransferBufferSize StreamProxyBufferSize { get; init; }
    public required int MaxPacketChannelCount { get; init; }
    public required TimeSpan MaxPacketChannelLifespan { get; init; }
    public required TimeSpan MinPacketChannelLifespan { get; init; }
    public required TimeSpan SessionTimeout { get; init; }
    public required TimeSpan UnstableTimeout { get; set; }
}