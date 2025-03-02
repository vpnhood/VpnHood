using System.Drawing;

namespace VpnHood.AppLib.Assets;

public class AppResources
{
    public byte[]? IpLocationZipData { get; set; }
    public byte[]? SpaZipData { get; set; }
    public Size WindowSize { get; set; } = new(400, 700);
    public AppStrings Strings { get; set; } = new();
    public AppColors Colors { get; set; } = new();
    public AppIcons Icons { get; set; } = new();

    public class AppStrings
    {
        public string AppName { get; set; } = Resources.AppName;
        public string Disconnect { get; set; } = Resources.Disconnect;
        public string Connect { get; set; } = Resources.Connect;
        public string Disconnected { get; set; } = Resources.Disconnected;
        public string Exit { get; set; } = Resources.Exit;
        public string Manage { get; set; } = Resources.Manage;
        public string MsgAccessKeyAdded { get; set; } = Resources.MsgAccessKeyAdded;
        public string MsgAccessKeyUpdated { get; set; } = Resources.MsgAccessKeyUpdated;
        public string MsgCantReadAccessKey { get; set; } = Resources.MsgCantReadAccessKey;
        public string MsgUnsupportedContent { get; set; } = Resources.MsgUnsupportedContent;
        public string Open { get; set; } = Resources.Open;
        public string OpenInBrowser { get; set; } = Resources.OpenInBrowser;
    }

    public class AppColors
    {
        public Color? NavigationBarColor { get; set; }
        public Color? WindowBackgroundColor { get; set; }
        public Color? ProgressBarColor { get; set; }
    }

    public class AppIcons
    {
        public IconData? BadgeConnectedIcon { get; set; } = new(Resources.BadgeConnectedIcon);
        public IconData? BadgeConnectingIcon { get; set; } = new(Resources.BadgeConnectingIcon);
        public IconData? SystemTrayConnectedIcon { get; set; } = new(Resources.VpnConnectedIcon);
        public IconData? SystemTrayConnectingIcon { get; set; } = new(Resources.VpnConnectingIcon);
        public IconData? SystemTrayDisconnectedIcon { get; set; } = new(Resources.VpnDisconnectedIcon);
    }

    public class IconData(byte[] data)
    {
        public byte[] Data { get; } = data;
    }

    public class ImageData(byte[] data)
    {
        public byte[] Data { get; } = data;
    }
}