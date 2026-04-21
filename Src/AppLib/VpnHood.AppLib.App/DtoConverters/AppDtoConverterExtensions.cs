using VpnHood.AppLib.Dtos;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
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
            DnsConfig = sessionInfo.DnsConfig,
            IsLocalNetworkAllowed = sessionInfo.IsLocalNetworkAllowed,
            ServerLocationInfo = sessionInfo.ServerLocationInfo?.ToAppDto(),
            ServerVersion = sessionInfo.ServerVersion,
            IsPremiumSession = sessionInfo.IsPremiumSession,
            SuppressedTo = sessionInfo.SuppressedTo,
            ClientPublicIpAddress = sessionInfo.ClientPublicIpAddress,
            CreatedTime = sessionInfo.CreatedTime,
            ChannelProtocols = sessionInfo.ChannelProtocols,
            IsTcpProxySupported = sessionInfo.IsTcpProxySupported,
            IsTcpPacketSupported = sessionInfo.IsTcpPacketSupported
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
            StreamTunnelledCount = sessionStatus.StreamTunnelledCount,
            StreamPassthruCount = sessionStatus.StreamPassthruCount,
            PacketChannelCount = sessionStatus.PacketChannelCount,
            UnstableCount = sessionStatus.UnstableCount,
            WaitingCount = sessionStatus.WaitingCount,
            CanExtendByRewardedAd = canExtendByRewardedAd,
            SessionMaxTraffic = sessionStatus.SessionMaxTraffic,
            SessionExpirationTime = sessionStatus.SessionExpirationTime,
            ActiveClientCount = sessionStatus.ActiveClientCount,
            ChannelProtocol = sessionStatus.ChannelProtocol,
            IsTcpProxy = sessionStatus.IsTcpProxy,
            IsTcpProxyByDomainFilter = sessionStatus.IsTcpProxyByDomainFilter,
            CanChangeTcpProxy = sessionStatus.CanChangeTcpProxy,
            IsDropQuic = sessionStatus.IsDropQuic
        };
    }

    public static ApiError ToAppDto(this ApiError apiError)
    {
        apiError = (ApiError)apiError.Clone();

        // remove sensitive info like access key 
        apiError.Data.Remove(nameof(SessionResponse));

        return apiError;
    }

    public static AppProxyEndPointManagerStatus ToAppDto(this ProxyEndPointManagerStatus sessionStatus)
    {
        return new AppProxyEndPointManagerStatus {
            SessionStatus = sessionStatus.SessionStatus,
            SucceededServerCount = sessionStatus.SucceededServerCount,
            FailedServerCount = sessionStatus.FailedServerCount,
            UnknownServerCount = sessionStatus.UnknownServerCount,
            DisabledServerCount = sessionStatus.DisabledServerCount
        };
    }

    public static AppServerLocationInfo ToAppDto(this ServerLocationInfo value)
    {
        return AppServerLocationInfo.FromInfo(value);
    }
}