using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Assets.ClassicSpa;
using VpnHood.AppLib.Assets.Ip2LocationLite;

namespace VpnHood.App.Client;

public static class ConnectAppResources
{
    private static readonly Lazy<AppResources> LazyResource = new(() => new AppResources {
        IpLocationZipData = Ip2LocationLiteDb.ZipData,
        SpaZipData = ConnectSpaResources.SpaZipData,
        Colors = new AppResources.AppColors {
            NavigationBarColor = ConnectSpaResources.NavigationBarColor,
            WindowBackgroundColor = ConnectSpaResources.WindowBackgroundColor,
            ProgressBarColor = ConnectSpaResources.ProgressBarColor
        },
        Icons = new AppResources.AppIcons {
            SystemTrayConnectedIcon = new AppResources.IconData(ConnectSpaResources.SystemTrayConnectedIcon),
            SystemTrayConnectingIcon = new AppResources.IconData(ConnectSpaResources.SystemTrayConnectingIcon),
            SystemTrayDisconnectedIcon = new AppResources.IconData(ConnectSpaResources.SystemTrayDisconnectedIcon)
        }
    });

    public static AppResources Resources => LazyResource.Value;
}