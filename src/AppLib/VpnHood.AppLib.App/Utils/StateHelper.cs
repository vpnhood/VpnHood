using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Utils;

internal static class StateHelper
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

    // The one place that says why a configured feature is not in effect. The gate itself is silent —
    // it runs on every state poll and every settings resolution — so this line, written once per
    // connection, is the whole story in the log.
    public static void LogPlanDroppedFeatures(UserSettings userSettings, IPremiumFeatureChecker premiumFeatureChecker)
    {
        var splitTunneling = userSettings.SplitTunneling;
        var dropped = new List<AppFeature>();

        if (splitTunneling.UseIpViaApp && !premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitIpViaApp))
            dropped.Add(AppFeature.SplitIpViaApp);

        if (splitTunneling.UseIpViaDevice && !premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitIpViaDevice))
            dropped.Add(AppFeature.SplitIpViaDevice);

        if (splitTunneling.UseDomain && !premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitDomain))
            dropped.Add(AppFeature.SplitDomain);

        if (splitTunneling.CountryMode is not SplitCountryMode.IncludeAll &&
            !premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitCountry))
            dropped.Add(AppFeature.SplitCountry);

        if (userSettings.DnsMode is DnsMode.AdapterDns && !VhUtils.IsNullOrEmpty(userSettings.DnsServers) &&
            !premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.CustomDns))
            dropped.Add(AppFeature.CustomDns);

        if (dropped.Count > 0)
            VhLogger.Instance.LogWarning(
                "Some features are configured but not applied, because the current plan does not include them. Features: {Features}",
                string.Join(", ", dropped));
    }

    public static TcpProxyUsageReason GetTcpProxyUsageReason(
        AppFeatures appFeatures,
        UserSettings userSettings,
        SessionInfo? sessionInfo,
        IPremiumFeatureChecker premiumFeatureChecker)
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

        // split-by-domain requires TcpProxy for SNI stream interception (effective: a split the
        // toggle silenced or the plan withheld is never applied, so it must not force the proxy path)
        if (userSettings.SplitTunneling.ToEffective(premiumFeatureChecker).UseDomain)
            return TcpProxyUsageReason.SplitDomainRequiredOn;

        return TcpProxyUsageReason.None;
    }

    // The EFFECTIVE split picture: every flag answers "is this split actually open in the CURRENT
    // state", which ToEffective already decides — the super toggle silences them all (except the
    // exempt pair) and the plan withholds what it does not include, so the UI holds no logic and
    // this method adds no gate of its own. The server's word counts only while the effective
    // unsupported-ip mode lets destinations out, and only when a session has revealed a declaration
    // that leaves public destinations out.
    public static SplitTunnelingState GetSplitTunnelingState(
        UserSettings userSettings,
        SessionInfo? sessionInfo,
        IPremiumFeatureChecker premiumFeatureChecker)
    {
        // both gates are already resolved here, so every flag below is a plain read
        var splitTunneling = userSettings.SplitTunneling.ToEffective(premiumFeatureChecker);

        // an empty exclude list excludes nothing; an include list leaves everything else outside
        var isAppSplit = splitTunneling.AppMode switch {
            SplitAppMode.Exclude => splitTunneling.Apps.Length > 0,
            SplitAppMode.Include => true,
            _ => false
        };

        var isCountrySplit = splitTunneling.CountryMode switch {
            SplitCountryMode.ExcludeMyCountry => true,
            SplitCountryMode.ExcludeList => splitTunneling.Countries.Length > 0,
            SplitCountryMode.IncludeList => true,
            _ => false
        };

        var isIpV6Split = splitTunneling.UnsupportedIpV6Mode is SplitUnsupportedIpMode.Exclude &&
                          sessionInfo?.IsIpV6SupportedByServer == false;

        // the server's word only splits while the effective mode lets unsupported destinations out —
        // the toggle forces Block, and a power user may have chosen Block even while splitting is allowed
        var isSplitByServer = splitTunneling.UnroutedIpMode is SplitUnsupportedIpMode.Exclude &&
                              sessionInfo?.IsTrafficSplitByServer == true;

        // SplitDnsMode is deliberately not a split of its own: DefaultRoute only lets DNS follow the
        // splits, so it can never leak unless one of the splits below is already open — and each of
        // those reports itself, DNS included. Two flags are reported for their pages but never join
        // IsSplittingTraffic, because neither exposes the public IP of the traffic that stays inside:
        // IsLocalNetworkSplit (LAN traffic stays on-link) and IsAppSplit (a per-app opt-out the user
        // chose app by app, leaving every other app's traffic in the tunnel).
        return new SplitTunnelingState {
            IsEnabled = splitTunneling.Enabled,
            IsAppSplit = isAppSplit,
            IsCountrySplit = isCountrySplit,
            IsIpViaAppSplit = splitTunneling.UseIpViaApp,
            IsIpViaDeviceSplit = splitTunneling.UseIpViaDevice,
            IsDomainSplit = splitTunneling.UseDomain,
            IsLocalNetworkSplit = splitTunneling.UseLocalNetwork,
            IsIpV6Split = isIpV6Split,
            IsSplitByServer = isSplitByServer,
            IsSplittingTraffic = isCountrySplit ||
                                 splitTunneling.UseIpViaApp || splitTunneling.UseIpViaDevice ||
                                 splitTunneling.UseDomain || isIpV6Split || isSplitByServer
        };
    }
}