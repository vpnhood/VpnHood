using System.Net;
using VpnHood.Core.Client.Abstractions;
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
    public required TimeSpan UnstableTimeout { get; init; }
    public required TimeSpan AutoWaitTimeout { get; init; }
    public required DnsConfig DnsConfig { get; init; }
    public required bool IsTcpProxySupported { get; init; }
    public required IPEndPoint? HostUdpEndPoint { get; init; }
    public required int RemoteMtu { get; init; }
    public required bool IsIpV6SupportedByServer { get; init; }
}