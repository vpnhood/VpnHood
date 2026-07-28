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
        if (userSettings.SplitTunneling.UseDomain)
            return TcpProxyUsageReason.SplitDomainRequiredOn;

        return TcpProxyUsageReason.None;
    }

    // Every option that can push traffic outside the VPN in the CURRENT state — the user's own splits
    // included, since each is an open door worth seeing in one list. Options that can not actually leak
    // right now are left out: the server's declaration counts only while unsupported IPs are excluded
    // rather than blocked, only when it leaves something out, and only when a session has revealed it.
    public static IReadOnlyList<AppLeakCause> GetLeakCauses(UserSettings userSettings, SessionInfo? sessionInfo)
    {
        var causes = new List<AppLeakCause>();
        var splitTunneling = userSettings.SplitTunneling;

        // an empty exclude list excludes nothing; an include list leaves everything else outside
        var isAppSplit = splitTunneling.AppMode switch {
            SplitAppMode.Exclude => splitTunneling.Apps.Length > 0,
            SplitAppMode.Include => true,
            _ => false
        };
        if (isAppSplit)
            causes.Add(AppLeakCause.SplitApps);

        var isCountrySplit = splitTunneling.CountryMode switch {
            SplitCountryMode.ExcludeMyCountry => true,
            SplitCountryMode.ExcludeList => splitTunneling.Countries.Length > 0,
            SplitCountryMode.IncludeList => true,
            _ => false
        };
        if (isCountrySplit)
            causes.Add(AppLeakCause.SplitCountry);

        if (splitTunneling.UseIpViaApp)
            causes.Add(AppLeakCause.SplitIpViaApp);

        if (splitTunneling.UseIpViaDevice)
            causes.Add(AppLeakCause.SplitIpViaDevice);

        if (splitTunneling.UseDomain)
            causes.Add(AppLeakCause.SplitDomain);

        if (splitTunneling.UseLocalNetwork)
            causes.Add(AppLeakCause.SplitLocalNetwork);

        // the server's word only leaks while the user lets unsupported destinations out
        if (splitTunneling.UnsupportedIpMode is SplitUnsupportedIpMode.Exclude &&
            sessionInfo?.IsTrafficSplitByServer == true)
            causes.Add(AppLeakCause.ServerSplitTraffic);

        // SplitDnsMode is deliberately not a cause of its own: DefaultRoute only lets DNS follow the
        // splits, so it can never leak unless one of the causes above is already open — and each of those
        // reports the leak, DNS included.
        return causes;
    }
}