using VpnHood.AppLib.Dtos;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.DtoConverters;

public static class AppDtoConverterExtensions
{
    public static AppConnectorStat ToAppDto(this ConnectorStatus connectorStatus)
    {
        return new AppConnectorStat {
            FreeConnectionCount = connectorStatus.FreeConnectionCount,
            ReusedConnectionFailedCount = connectorStatus.ReusedConnectionFailedCount,
            ReusedConnectionSucceededCount = connectorStatus.ReusedConnectionSucceededCount,
            CreatedConnectionCount = connectorStatus.CreatedConnectionCount,
            RequestCount = connectorStatus.RequestCount
        };
    }

    public static AppSessionInfo ToAppDto(this SessionInfo sessionInfo)
    {
        return new AppSessionInfo {
            AccessInfo = sessionInfo.AccessInfo,
            IsUdpChannelSupported = sessionInfo.IsUdpChannelSupported,
            IsDnsServersAccepted = sessionInfo.IsDnsServersAccepted,
            IsLocalNetworkAllowed = sessionInfo.IsLocalNetworkAllowed,
            ServerLocationInfo = sessionInfo.ServerLocationInfo,
            ServerVersion = sessionInfo.ServerVersion,
            IsPremiumSession = sessionInfo.IsPremiumSession,
            SuppressedTo = sessionInfo.SuppressedTo,
            DnsServers = sessionInfo.DnsServers,
            ClientPublicIpAddress = sessionInfo.ClientPublicIpAddress,
            CreatedTime = sessionInfo.CreatedTime,
            IsTcpPacketSupported = sessionInfo.IsTcpPacketSupported,
            IsTcpProxySupported = sessionInfo.IsTcpProxySupported,
        };
    }

    public static AppSessionStatus ToAppDto(this SessionStatus sessionStatus, bool canExtendByRewardedAd)
    {
        return new AppSessionStatus {
            ConnectorStat = sessionStatus.ConnectorStatus.ToAppDto(),
            Speed = sessionStatus.Speed,
            SessionTraffic = sessionStatus.SessionTraffic,
            SessionSplitTraffic = sessionStatus.SessionSplitTraffic,
            CycleTraffic = sessionStatus.CycleTraffic,
            TotalTraffic = sessionStatus.TotalTraffic,
            TcpTunnelledCount = sessionStatus.TcpTunnelledCount,
            TcpPassthruCount = sessionStatus.TcpPassthruCount,
            PacketChannelCount = sessionStatus.PacketChannelCount,
            UnstableCount = sessionStatus.UnstableCount,
            WaitingCount = sessionStatus.WaitingCount,
            IsUdpMode = sessionStatus.IsUdpMode,
            IsTcpProxy = sessionStatus.IsTcpProxy,
            CanExtendByRewardedAd = canExtendByRewardedAd,
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