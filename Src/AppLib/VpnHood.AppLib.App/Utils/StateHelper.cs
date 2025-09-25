using System.Diagnostics.CodeAnalysis;
using System.Linq;
using VpnHood.AppLib.Services.Ads;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Utils;

public static class StateHelper
{
    public static ChannelProtocol[] GetServerChannelProtocols(AppFeatures features, SessionInfo? sessionInfo)
    {
        return features.ChannelProtocols.Except(sessionInfo?.ChannelProtocols ?? []).ToArray();
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