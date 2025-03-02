using System.Drawing;

namespace VpnHood.AppLib.Assets;

public static class DefaultAppResources
{
    private static readonly Lazy<AppResources> LazyResource = new(() => new AppResources {
        SpaZipData = EmbeddedResources.SPA,
        Colors = new AppResources.AppColors {
            NavigationBarColor = Color.FromArgb(18, 34, 114),
            WindowBackgroundColor = Color.FromArgb(0x19, 0x40, 0xb0),
            ProgressBarColor = Color.FromArgb(35, 201, 157)
        },
        Icons = new AppResources.AppIcons {
            SystemTrayConnectedIcon = new AppResources.IconData(EmbeddedResources.VpnConnectedIcon),
            SystemTrayConnectingIcon = new AppResources.IconData(EmbeddedResources.VpnConnectingIcon),
            SystemTrayDisconnectedIcon = new AppResources.IconData(EmbeddedResources.VpnDisconnectedIcon)
        }
    });

    public static AppResources Resources => LazyResource.Value;
}