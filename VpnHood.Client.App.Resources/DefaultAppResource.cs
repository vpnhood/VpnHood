using System.Drawing;

namespace VpnHood.Client.App.Resources;

public static class DefaultAppResource 
{
    public static AppResource Resource { get; } = new()
    {
        SpaZipData = EmbeddedResource.SPA,
        Colors = new AppResource.AppColors
        {
            NavigationBarColor = Color.FromArgb(18, 34, 114),
            WindowBackgroundColor = Color.FromArgb(0x19, 0x40, 0xb0),
            ProgressBarColor = Color.FromArgb(35, 201, 157),
        },
        Icons = new AppResource.AppIcons
        {
            SystemTrayConnectedIcon = new AppResource.IconData(EmbeddedResource.VpnConnectedIcon),
            SystemTrayConnectingIcon = new AppResource.IconData(EmbeddedResource.VpnConnectingIcon),
            SystemTrayDisconnectedIcon = new AppResource.IconData(EmbeddedResource.VpnDisconnectedIcon)
        }
    };
}

