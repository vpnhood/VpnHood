using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ConnectorServices;

namespace VpnHood.Core.Client.ApiControllers;

internal static class DtoConverters
{

    public static ConnectionInfo ToDto(this IConnectionInfo connectionInfo)
    {
        var dto = new ConnectionInfo {
            SessionInfo = connectionInfo.SessionInfo,
            SessionStatus = connectionInfo.SessionStatus?.ToDto(),
            ClientState = connectionInfo.ClientState,
            Error = connectionInfo.Error,
            ErrorCode = connectionInfo.ErrorCode
        };

        return dto;
    }

    public static SessionStatus ToDto(this ISessionStatus sessionStatus)
    {
        var ret = new SessionStatus {
            ConnectorStat = sessionStatus.ConnectorStat.ToDto(),
            ActiveClientCount = sessionStatus.ActiveClientCount,
            CanExtendByRewardedAd = sessionStatus.CanExtendByRewardedAd,
            CycleTraffic = sessionStatus.CycleTraffic,
            DatagramChannelCount = sessionStatus.DatagramChannelCount,
            IsUdpMode = sessionStatus.IsUdpMode,
            SessionExpirationTime = sessionStatus.SessionExpirationTime,
            SessionMaxTraffic = sessionStatus.SessionMaxTraffic,
            SessionTraffic = sessionStatus.SessionTraffic,
            TotalTraffic = sessionStatus.TotalTraffic,
            Speed = sessionStatus.Speed,
            TcpPassthruCount = sessionStatus.TcpPassthruCount,
            TcpTunnelledCount = sessionStatus.TcpTunnelledCount,
            AdRequest = sessionStatus.AdRequest
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