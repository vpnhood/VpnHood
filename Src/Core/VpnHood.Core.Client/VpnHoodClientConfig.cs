using System.Net;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Client;

public class VpnHoodClientConfig
{
    // 10: Support RedirectHostServerTokens (7.5.770)
    // 11: New UDP AesGcm encryption 
    // 12: fix server WebSocketNoHandshake 2026/02
    public static int MaxProtocolVersion => 12;
    public static int MinProtocolVersion => 8;
    public required Version Version { get; init; }
    public required bool AutoDisposeVpnAdapter { get; init; }
    public required int MaxPacketChannelCount { get; init; }
    public required TimeSpan MinPacketChannelLifespan { get; init; }
    public required TimeSpan MaxPacketChannelLifespan { get; init; }
    public required bool AllowAnonymousTracker { get; init; }
    public required TimeSpan TcpConnectTimeout { get; init; }
    public required ConnectPlanId PlanId { get; init; }
    public required string? AccessCode { get; init; }
    public required string[]? IncludeApps { get; init; }
    public required string[]? ExcludeApps { get; init; }
    public required IReadOnlyList<IpRange> IncludeIpRangesByDevice { get; init; }
    public required IReadOnlyList<IpRange> IncludeIpRangesByApp { get; init; }
    public required IReadOnlyList<IpRange> BlockIpRangesByApp { get; init; }
    public required IReadOnlyList<IPAddress>? DnsServers { get; init; }
    public required TimeSpan SessionTimeout { get; init; }
    public required TimeSpan AutoWaitTimeout { get; init; }
    public required TimeSpan UnstableTimeout { get; init; }
    public required TransferBufferSize StreamProxyBufferSize { get; init; }
    public required TransferBufferSize? UdpProxyBufferSize { get; init; }
    public required string ClientId { get; init; }
    public required bool IncludeLocalNetwork { get; init; }
    public required string? SessionName { get; init; }
    public required string UserAgent { get; init; }
    public required bool AllowTcpReuse { get; init; }
    public required UserReview? UserReview { get; init; }
    public required bool IsTcpProxySupported { get; init; }
    public required bool UseWebSocket { get; init; }
    public required IPAddress TcpProxyCatcherAddressIpV4 { get; init; }
    public required IPAddress TcpProxyCatcherAddressIpV6 { get; init; }
    public required bool UseTcpProxy { get; init; }
    public required bool DropUdp { get; init; }
    public required bool DropQuic { get; init; }
}