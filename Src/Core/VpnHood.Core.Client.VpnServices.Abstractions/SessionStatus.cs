using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public class SessionStatus
{
    public required ConnectorStat ConnectorStat { get; init; }
    public required Traffic Speed { get; init; }
    public required Traffic SessionTraffic { get; init; }
    public required Traffic CycleTraffic { get; init; }
    public required Traffic TotalTraffic { get; init; }
    public required int TcpTunnelledCount { get; init; }
    public required int TcpPassthruCount { get; init; }
    public required int DatagramChannelCount { get; init; }
    public required bool IsUdpMode { get; init; }
    public required bool CanExtendByRewardedAd { get; init; }
    public required long SessionMaxTraffic { get; init; }
    public required DateTime? SessionExpirationTime { get; init; }
    public required int? ActiveClientCount { get; init; }
    public required AdRequest? AdRequest { get; init; }
}