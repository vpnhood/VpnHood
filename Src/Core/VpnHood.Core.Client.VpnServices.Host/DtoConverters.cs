using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

internal static class DtoConverters
{
    public static SessionStatus ToDto(this ISessionStatus sessionStatus)
    {
        var ret = new SessionStatus {
            ConnectorStatus = sessionStatus.ConnectorStatus.ToDto(),
            ActiveClientCount = sessionStatus.ActiveClientCount,
            CanExtendByRewardedAd = sessionStatus.CanExtendByRewardedAd,
            CycleTraffic = sessionStatus.CycleTraffic,
            PacketChannelCount = sessionStatus.ActivePacketChannelCount,
            SessionExpirationTime = sessionStatus.SessionExpirationTime,
            SessionMaxTraffic = sessionStatus.SessionMaxTraffic,
            SessionTraffic = sessionStatus.SessionTraffic,
            SessionSplitTraffic = sessionStatus.SessionSplitTraffic,
            TotalTraffic = sessionStatus.TotalTraffic,
            Speed = sessionStatus.Speed,
            TcpPassthruCount = sessionStatus.TcpPassthruCount,
            TcpTunnelledCount = sessionStatus.TcpTunnelledCount,
            UnstableCount = sessionStatus.UnstableCount,
            WaitingCount = sessionStatus.WaitingCount,
            UserReviewRecommended = sessionStatus.UserReviewRecommended,
            IsDnsOverTlsDetected = sessionStatus.IsDnsOverTlsDetected,
            ProxyManagerStatus = sessionStatus.ProxyManagerStatus,
            ChannelProtocol = sessionStatus.ChannelProtocol
        };

        return ret;
    }

    public static ConnectorStatus ToDto(this ClientConnectorStatus connectorStatus)
    {
        var dto = new ConnectorStatus {
            CreatedConnectionCount = connectorStatus.CreatedConnectionCount,
            FreeConnectionCount = connectorStatus.FreeConnectionCount,
            RequestCount = connectorStatus.RequestCount,
            ReusedConnectionFailedCount = connectorStatus.ReusedConnectionFailedCount,
            ReusedConnectionSucceededCount = connectorStatus.ReusedConnectionSucceededCount
        };

        return dto;
    }
}