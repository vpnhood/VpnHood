using System.Drawing;

namespace VpnHood.AppLib.Resources;

public static class DefaultAppResource
{
    private static readonly Lazy<AppResource> LazyResource = new(() => new AppResource {
        SpaZipData = EmbeddedResource.SPA,
        Colors = new AppResource.AppColors {
            NavigationBarColor = Color.FromArgb(18, 34, 114),
            WindowBackgroundColor = Color.FromArgb(0x19, 0x40, 0xb0),
            ProgressBarColor = Color.FromArgb(35, 201, 157)
        },
        Icons = new AppResource.AppIcons {
            SystemTrayConnectedIcon = new AppResource.IconData(EmbeddedResource.VpnConnectedIcon),
            SystemTrayConnectingIcon = new AppResource.IconData(EmbeddedResource.VpnConnectingIcon),
            SystemTrayDisconnectedIcon = new AppResource.IconData(EmbeddedResource.VpnDisconnectedIcon)
        }
    });

    public static AppResource Resources => LazyResource.Value;
}