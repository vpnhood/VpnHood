using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

internal static class DtoConverters
{
    public static SessionStatus ToDto(this ISessionStatus sessionStatus)
    {
        var ret = new SessionStatus {
            ConnectorStat = sessionStatus.ConnectorStat.ToDto(),
            ActiveClientCount = sessionStatus.ActiveClientCount,
            CanExtendByRewardedAd = sessionStatus.CanExtendByRewardedAd,
            CycleTraffic = sessionStatus.CycleTraffic,
            PacketChannelCount = sessionStatus.ActivePacketChannelCount,
            IsUdpMode = sessionStatus.IsUdpMode,
            SessionExpirationTime = sessionStatus.SessionExpirationTime,
            SessionMaxTraffic = sessionStatus.SessionMaxTraffic,
            SessionTraffic = sessionStatus.SessionTraffic,
            SessionSplitTraffic = sessionStatus.SessionSplitTraffic,
            TotalTraffic = sessionStatus.TotalTraffic,
            Speed = sessionStatus.Speed,
            TcpPassthruCount = sessionStatus.TcpPassthruCount,
            TcpTunnelledCount = sessionStatus.TcpTunnelledCount,
            IsUserReviewRecommended = sessionStatus.IsUserReviewRecommended
        };

        return ret;
    }

    public static ConnectorStat ToDto(this ClientConnectorStat connectorStat)
    {
        var dto = new ConnectorStat {
            CreatedConnectionCount = connectorStat.CreatedConnectionCount,
            FreeConnectionCount = connectorStat.FreeConnectionCount,
            RequestCount = connectorStat.RequestCount,
            ReusedConnectionFailedCount = connectorStat.ReusedConnectionFailedCount,
            ReusedConnectionSucceededCount = connectorStat.ReusedConnectionSucceededCount
        };

        return dto;
    }
}