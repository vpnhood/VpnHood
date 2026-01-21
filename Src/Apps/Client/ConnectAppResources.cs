using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Assets.ClassicSpa;
using VpnHood.AppLib.Assets.Ip2LocationLite;
using VpnHood.Core.Toolkit.Graphics;

namespace VpnHood.App.Client;

public static class ConnectAppResources
{
    private static readonly Lazy<AppResources> LazyResource = new(() => new AppResources {
        IpLocationZipData = Ip2LocationLiteDb.ZipData,
        SpaZipData = ConnectSpaResources.SpaZipData,
        Colors = new AppResources.AppColors {
            NavigationBarColor = VhColor.Parse(ConnectSpaResources.NavigationBarColor),
            WindowBackgroundColor = VhColor.Parse(ConnectSpaResources.WindowBackgroundColor),
            ProgressBarColor = VhColor.Parse(ConnectSpaResources.ProgressBarColor)
        },
        Icons = new AppResources.AppIcons {
            SystemTrayConnectedIcon = new AppResources.IconData(ConnectSpaResources.SystemTrayConnectedIcon),
            SystemTrayConnectingIcon = new AppResources.IconData(ConnectSpaResources.SystemTrayConnectingIcon),
            SystemTrayDisconnectedIcon = new AppResources.IconData(ConnectSpaResources.SystemTrayDisconnectedIcon)
        }
    });

    public static AppResources Resources => LazyResource.Value;

    public static AppFeature[] PremiumFeatures { get; } = [
        AppFeature.CustomDns,
        AppFeature.AlwaysOn,
        AppFeature.QuickLaunch,
        AppFeature.SplitByIpViaApp,
        AppFeature.SplitByIpViaDevice
    ];
}