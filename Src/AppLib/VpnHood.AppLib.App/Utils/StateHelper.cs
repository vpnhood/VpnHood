using System.Diagnostics.CodeAnalysis;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Utils;

public static class StateHelper
{
    public static bool IsLongRunningState([NotNullWhen(true)] ConnectionInfo? connectionInfo)
    {
        return FastDateTime.Now - connectionInfo?.ClientStateChangedTime > TimeSpan.FromMilliseconds(2000);
    }

    public static int? GetProgress(ConnectionInfo? connectionInfo, AppAdService adService)
    {
        if (!IsLongRunningState(connectionInfo))
            return null;

        // show ad progress if waiting for ad
        if (connectionInfo.ClientState is ClientState.WaitingForAd)
            return adService.LoadAdProgress?.Percentage;

        // show progress only if total is at least 3 to avoid showing 0% and 100% too early
        var progress = connectionInfo.ClientStateProgress;
        if (progress?.Total > 2)
            return progress.Value.Percentage;

        return null;
    }

    public static AppServerLocationInfo? GetServerLocationInfo(
        SessionInfo? sessionInfo,
        ClientProfileInfo? clientProfileInfo)
    {
        // get session server location info
        var sessionServerLocationInfo = sessionInfo?.ServerLocationInfo;
        if (sessionServerLocationInfo != null) {
            return AppServerLocationInfo.FromInfo(
                sessionServerLocationInfo,
                clientProfileInfo?.HasMultipleRegion(sessionServerLocationInfo.CountryCode) == true);
        }

        // return user selected 
        if (clientProfileInfo?.SelectedLocationInfo is null)
            return null;

        return
             AppServerLocationInfo.FromInfo(
                 clientProfileInfo.SelectedLocationInfo,
                 clientProfileInfo.HasMultipleRegion(clientProfileInfo.SelectedLocationInfo.CountryCode));

    }

    public static TcpProxyUsageReason GetTcpProxyUsageReason(
        AppFeatures appFeatures,
        UserSettings userSettings,
        SessionInfo? sessionInfo)
    {
        // client platform does not support TcpProxy at all
        if (!appFeatures.IsTcpProxySupported)
            return TcpProxyUsageReason.ClientNotSupported;

        // server requires TcpProxy because it cannot deliver raw TCP packets
        if (sessionInfo is { IsTcpPacketSupported: false })
            return TcpProxyUsageReason.ServerRequiredOn;

        // server does not support TcpProxy
        if (sessionInfo is { IsTcpProxySupported: false })
            return TcpProxyUsageReason.ServerRequiredOff;

        // split-by-domain requires TcpProxy for SNI stream interception
        if (userSettings.UseSplitByDomain)
            return TcpProxyUsageReason.SplitByDomainRequiredOn;

        return TcpProxyUsageReason.None;
    }
}