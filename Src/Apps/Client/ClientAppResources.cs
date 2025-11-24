using System;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Assets.ClassicSpa;
using VpnHood.AppLib.Assets.Ip2LocationLite;
using VpnHood.Core.Toolkit.Graphics;

namespace VpnHood.App.Client;

public static class ClientAppResources
{
    private static readonly Lazy<AppResources> LazyResource = new(() => new AppResources {
        IpLocationZipData = Ip2LocationLiteDb.ZipData,
        SpaZipData = ClassicSpaResources.SpaZipData,
        Colors = new AppResources.AppColors {
            NavigationBarColor = VhColor.Parse(ClassicSpaResources.NavigationBarColor),
            WindowBackgroundColor = VhColor.Parse(ClassicSpaResources.WindowBackgroundColor),
            ProgressBarColor = VhColor.Parse(ClassicSpaResources.ProgressBarColor)
        },
        Icons = new AppResources.AppIcons {
            SystemTrayConnectedIcon = new AppResources.IconData(ClassicSpaResources.SystemTrayConnectedIcon),
            SystemTrayConnectingIcon = new AppResources.IconData(ClassicSpaResources.SystemTrayConnectingIcon),
            SystemTrayDisconnectedIcon = new AppResources.IconData(ClassicSpaResources.SystemTrayDisconnectedIcon)
        }
    });

    public static AppResources Resources => LazyResource.Value;
}