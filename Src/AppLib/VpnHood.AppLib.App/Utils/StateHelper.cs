using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.AppLib.Utils;

public static class StateHelper
{
    public static bool IsTcpProxy(AppFeatures features, UserSettings userSettings, SessionStatus? sessionStatus)
    {
        return sessionStatus?.IsTcpProxy ?? 
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