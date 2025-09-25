﻿using System.Diagnostics.CodeAnalysis;
using VpnHood.AppLib.Services.Ads;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Utils;

public static class StateHelper
{
    public static ServerChannelProtocols GetServerChannelProtocols(AppFeatures features, SessionInfo? sessionInfo)
    {
        if (sessionInfo == null)
            return new ServerChannelProtocols {
                Udp = true,
                Tcp = true,
                TcpProxyAndUdp = features.IsTcpProxySupported,
                TcpProxy = features.IsTcpProxySupported,
                TcpProxyAndDropQuick = features.IsTcpProxySupported,
            };

        return new ServerChannelProtocols {
            Udp = sessionInfo is { IsUdpChannelSupported: true, IsTcpPacketSupported: true },
            Tcp = sessionInfo.IsTcpPacketSupported,
            TcpProxyAndUdp = sessionInfo is { IsUdpChannelSupported: true, IsTcpProxySupported: true } && features.IsTcpProxySupported,
            TcpProxy = sessionInfo.IsTcpProxySupported && features.IsTcpProxySupported,
            TcpProxyAndDropQuick = sessionInfo.IsTcpProxySupported && features.IsTcpProxySupported
        };
    }

    public static bool IsLongRunningState([NotNullWhen(true)] ConnectionInfo? connectionInfo)
    {
        return FastDateTime.Now - connectionInfo?.ClientStateChangedTime > TimeSpan.FromMilliseconds(2000);
    }

    public static int? GetProgress(ConnectionInfo? connectionInfo, AppAdService adService)
    {
        if (!IsLongRunningState(connectionInfo))
            return null;

        // show ad progress if waiting for ad
        if (connectionInfo.ClientState is ClientState.WaitingForAd or ClientState.WaitingForAdEx)
            return adService.LoadAdProgress?.Percentage;

        // show progress only if total is at least 3 to avoid showing 0% and 100% too early
        var progress = connectionInfo.ClientStateProgress;
        if (progress?.Total > 2)
            return progress.Value.Percentage;

        return null;
    }

}