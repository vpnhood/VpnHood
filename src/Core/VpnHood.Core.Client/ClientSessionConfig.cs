using VpnHood.Core.Toolkit.Net;
using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

public class ClientSessionConfig
{
    public required VpnAdapterOptions AdapterOptions { get; init; }
    public required ulong SessionId { get; init; }
    public required ReadOnlyMemory<byte> SessionKey { get; init; }
    public required ClientTransportOptions Transport { get; init; }
    public required int MaxPacketChannelCount { get; init; }
    public required Traffic? MaxSpeedMbps { get; init; }
    public required TimeSpan MaxPacketChannelLifespan { get; init; }
    public required TimeSpan MinPacketChannelLifespan { get; init; }
    public required DnsConfig DnsConfig { get; init; }
    public required bool IsTcpProxySupported { get; init; }
    public required IPEndPoint? HostTcpEndPoint { get; init; }
    public required IPEndPoint? HostUdpEndPoint { get; init; }
    public required IPEndPoint? HostQuicEndPoint { get; init; }
    public required int Mtu { get; init; }
    public required bool IsIpV6SupportedByServer { get; init; }
    public required AdRequirement AdRequirement { get; init; }
}