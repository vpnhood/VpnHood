using System.Drawing;

namespace VpnHood.Client.App.Resources;

public static class VpnHoodAppResource 
{
    public static AppResources Resources { get; } = new()
    {
        SpaZipData = Resource.SPA,
        Colors = new AppResources.AppColors
        {
            NavigationBarColor = Color.FromArgb(18, 34, 114),
            WindowBackgroundColor = Color.FromArgb(0x19, 0x40, 0xb0)
        },
        Icons = new AppResources.AppIcons
        {
            SystemTrayConnectedIcon = new AppResources.IconData(Resource.VpnConnectedIcon),
            SystemTrayConnectingIcon = new AppResources.IconData(Resource.VpnConnectingIcon),
            SystemTrayDisconnectedIcon = new AppResources.IconData(Resource.VpnDisconnectedIcon)
        }
    };
}

