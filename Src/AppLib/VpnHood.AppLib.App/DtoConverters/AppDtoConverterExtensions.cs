﻿using VpnHood.AppLib.Dtos;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ConnectorServices;

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

    public static AppSessionStatus ToAppDto(this ISessionStatus sessionStatus)
    {
        return new AppSessionStatus {
            ConnectorStat = sessionStatus.ConnectorStat.ToAppDto(),
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