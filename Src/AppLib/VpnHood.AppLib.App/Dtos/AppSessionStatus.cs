using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.AppLib.Dtos;

public class AppSessionStatus
{
    public required AppConnectorStat ConnectorStat { get; init; }
    public required Traffic Speed { get; init; }
    public required Traffic SessionTraffic { get; init; }
    public required Traffic CycleTraffic { get; init; }
    public required Traffic TotalTraffic { get; init; }
    public required int TcpTunnelledCount { get; init; }
    public required int TcpPassthruCount { get; init; }
    public required int DatagramChannelCount { get; init; }
    public required bool IsUdpMode { get; init; }
    public required bool IsWaitingForAd { get; init; }
    public required bool CanExtendByRewardedAd { get; init; }
    public required long SessionMaxTraffic { get; init; }
    public required DateTime? SessionExpirationTime { get; init; }
    public required int? ActiveClientCount { get; init; }

    public static AppSessionStatus Create(ISessionStatus sessionStatus)
    {
        return new AppSessionStatus {
            ConnectorStat = AppConnectorStat.Create(sessionStatus.ConnectorStat),
            Speed = sessionStatus.Speed,
            SessionTraffic = sessionStatus.SessionTraffic,
            CycleTraffic = sessionStatus.CycleTraffic,
            TotalTraffic = sessionStatus.TotalTraffic,
            TcpTunnelledCount = sessionStatus.TcpTunnelledCount,
            TcpPassthruCount = sessionStatus.TcpPassthruCount,
            DatagramChannelCount = sessionStatus.DatagramChannelCount,
            IsUdpMode = sessionStatus.IsUdpMode,
            IsWaitingForAd = sessionStatus.IsWaitingForAd,
            CanExtendByRewardedAd = sessionStatus.CanExtendByRewardedAd,
            SessionMaxTraffic = sessionStatus.SessionMaxTraffic,
            SessionExpirationTime = sessionStatus.SessionExpirationTime,
            ActiveClientCount = sessionStatus.ActiveClientCount
        };
    }
}