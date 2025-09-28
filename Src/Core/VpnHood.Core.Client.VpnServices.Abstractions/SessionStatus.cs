using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public class SessionStatus
{
    public required ConnectorStatus ConnectorStatus { get; init; }
    public required ProxyManagerStatus ProxyManagerStatus { get; init; }
    public required Traffic Speed { get; init; }
    public required Traffic SessionTraffic { get; init; }
    public required Traffic SessionSplitTraffic { get; init; }
    public required Traffic CycleTraffic { get; init; }
    public required Traffic TotalTraffic { get; init; }
    public required int TcpTunnelledCount { get; init; }
    public required int TcpPassthruCount { get; init; }
    public required int UnstableCount { get; init; }
    public required int WaitingCount { get; init; }
    public required int PacketChannelCount { get; init; }
    public required bool CanExtendByRewardedAd { get; init; }
    public required long SessionMaxTraffic { get; init; }
    public required DateTime? SessionExpirationTime { get; init; }
    public required int? ActiveClientCount { get; init; }
    public required int UserReviewRecommended { get; init; }
    public required bool IsDnsOverTlsDetected { get; init; }
    public required bool IsTcpProxy { get; init; }
    public required bool IsDropQuic { get; set; }
    public required ChannelProtocol ChannelProtocol { get; init; }
}