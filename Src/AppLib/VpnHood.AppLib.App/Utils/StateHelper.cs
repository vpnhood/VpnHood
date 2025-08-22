using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;

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

}