using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.ApiClients;

namespace VpnHood.Core.Client.ApiControllers;

internal static class DtoConverters
{
    public static ConnectionInfo ToConnectionInfo(this VpnHoodClient vpnHoodClient, ApiController apiController)
    {
        var dto = new ConnectionInfo {
            SessionInfo = vpnHoodClient.SessionInfo,
            SessionStatus = vpnHoodClient.SessionStatus?.ToDto(),
            ClientState = vpnHoodClient.State,
            Error = vpnHoodClient.LastException?.ToApiError(),
            ApiEndPoint = apiController.ApiEndPoint,
            ApiKey = apiController.ApiKey
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