using VpnHood.AppLib.Api.App;

namespace VpnHood.App.Client;

public static class ConnectAppResources
{
    public static AppFeature[] PremiumFeatures { get; } = [
        AppFeature.CustomDns,
        AppFeature.AlwaysOn,
        AppFeature.QuickLaunch,
        AppFeature.SplitIpViaApp,
        AppFeature.SplitIpViaDevice,
        AppFeature.SplitDomain
    ];
}
