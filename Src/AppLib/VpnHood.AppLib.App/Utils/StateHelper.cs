using System.Diagnostics.CodeAnalysis;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Utils;

public static class StateHelper
{
    public static bool IsTcpProxy(AppFeatures features, UserSettings userSettings, ConnectionInfo? connectionInfo,
        bool isReconfiguring)
    {
        var sessionStatus = connectionInfo?.SessionStatus;
        var sessionInfo = connectionInfo?.SessionInfo;

        // let use the current user setting if change is possible
        // it makes sure the UI and the actual setting are in sync during reconfiguration till real state is known
        if (sessionStatus != null && CanChangeTcpProxy(features, sessionInfo))
            return userSettings.UseTcpProxy;

        return
            sessionStatus?.IsTcpProxy ??
            userSettings.UseTcpProxy &&
            features.IsTcpProxySupported;
    }

    public static bool CanChangeTcpProxy(AppFeatures features, SessionInfo? sessionInfo)
    {
        if (!features.IsTcpProxySupported || sessionInfo == null)
            return features.IsTcpProxySupported;

        return sessionInfo is { IsTcpProxySupported: true, IsTcpPacketSupported: true };
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