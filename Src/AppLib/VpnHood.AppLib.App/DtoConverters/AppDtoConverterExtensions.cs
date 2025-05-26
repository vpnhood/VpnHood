using VpnHood.AppLib.Dtos;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.DtoConverters;

public static class AppDtoConverterExtensions
{
    public static AppConnectorStat ToAppDto(this ConnectorStat connectorStat)
    {
        return new AppConnectorStat {
            FreeConnectionCount = connectorStat.FreeConnectionCount,
            ReusedConnectionFailedCount = connectorStat.ReusedConnectionFailedCount,
            ReusedConnectionSucceededCount = connectorStat.ReusedConnectionSucceededCount,
            CreatedConnectionCount = connectorStat.CreatedConnectionCount,
            RequestCount = connectorStat.RequestCount
        };
    }

    public static AppSessionInfo ToAppDto(this SessionInfo sessionInfo)
    {
        return new AppSessionInfo {
            AccessInfo = sessionInfo.AccessInfo,
            IsUdpChannelSupported = sessionInfo.IsUdpChannelSupported,
            IsDnsServersAccepted = sessionInfo.IsDnsServersAccepted,
            ServerLocationInfo = sessionInfo.ServerLocationInfo,
            ServerVersion = sessionInfo.ServerVersion,
            IsPremiumSession = sessionInfo.IsPremiumSession,
            SuppressedTo = sessionInfo.SuppressedTo,
            DnsServers = sessionInfo.DnsServers,
            ClientPublicIpAddress = sessionInfo.ClientPublicIpAddress
        };
    }

    public static AppSessionStatus ToAppDto(this SessionStatus sessionStatus)
    {
        return new AppSessionStatus {
            ConnectorStat = sessionStatus.ConnectorStat.ToAppDto(),
            Speed = sessionStatus.Speed,
            SessionTraffic = sessionStatus.SessionTraffic,
            SessionSplitTraffic = sessionStatus.SessionSplitTraffic,
            CycleTraffic = sessionStatus.CycleTraffic,
            TotalTraffic = sessionStatus.TotalTraffic,
            TcpTunnelledCount = sessionStatus.TcpTunnelledCount,
            TcpPassthruCount = sessionStatus.TcpPassthruCount,
            PacketChannelCount = sessionStatus.PacketChannelCount,
            IsUdpMode = sessionStatus.IsUdpMode,
            IsWaitingForAd = sessionStatus.AdRequest != null,
            CanExtendByRewardedAd = sessionStatus.CanExtendByRewardedAd,
            SessionMaxTraffic = sessionStatus.SessionMaxTraffic,
            SessionExpirationTime = sessionStatus.SessionExpirationTime,
            ActiveClientCount = sessionStatus.ActiveClientCount
        };
    }

    public static ApiError ToAppDto(this ApiError apiError)
    {
        apiError = (ApiError)apiError.Clone();

        // remove sensitive info like access key 
        apiError.Data.Remove(nameof(SessionResponse));

        return apiError;
    }
}