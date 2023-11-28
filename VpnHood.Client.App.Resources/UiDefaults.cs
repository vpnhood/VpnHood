﻿using System.Drawing;

namespace VpnHood.Client.App.Resources;

public static class UiDefaults 
{
    public static AppResources AppResources { get; } = new()
    {
        SpaZipData = UiResource.SPA,
        Colors = new AppResources.AppColors
        {
            WindowBackgroundBottomColor = Color.FromArgb(18, 34, 114),
            WindowBackgroundColor = Color.FromArgb(0x19, 0x40, 0xb0),
        },
        Icons = new AppResources.AppIcons
        {
            AppIcon = new AppResources.IconData(UiResource.VpnHoodIcon),
            ConnectedIcon = new AppResources.IconData(UiResource.VpnConnectedIcon),
            ConnectingIcon = new AppResources.IconData(UiResource.VpnConnectingIcon),
            DisconnectedIcon = new AppResources.IconData(UiResource.VpnDisconnectedIcon),
        }
    };
}

