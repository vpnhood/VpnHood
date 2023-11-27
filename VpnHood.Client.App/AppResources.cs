using System.Drawing;

namespace VpnHood.Client.App;

public class AppResources
{
    public byte[]? SpaZipData { get; set; }
    public Size WindowSize { get; set; } = new(400, 700);
    public AppStrings Strings { get; set; } = new();
    public AppColors Colors { get; set; } = new();
    public AppIcons Icons { get; set; } = new();

    public class AppStrings
    {
        public string AppName { get; set; } = "VpnHood!";
        public string Disconnect { get; set; } = "Disconnect";
        public string Connect { get; set; } = "Connect";
        public string Disconnected { get; set; } = "Disconnected";
        public string Exit { get; set; } = "Exit";
        public string Manage { get; set; } = "Manage";
        public string MsgAccessKeyAdded { get; set; } = "{0} Access key has been added.";
        public string MsgAccessKeyUpdated { get; set; } = "{0} access key has been updated.";
        public string MsgCantReadAccessKey { get; set; } = "Could not read the access key.";
        public string MsgUnsupportedContent { get; set; } = "Unsupported file type.";
        public string Open { get; set; } = "Open";
        public string OpenInBrowser { get; set; } = "Open in browser";
    }

    public class AppColors
    {
        public Color? WindowBackgroundBottomColor { get; set; }
        public Color? WindowBackgroundColor { get; set; }
    }

    public class AppIcons
    {
        public IconData? AppIcon { get; set; }
        public IconData? BadgeConnectedIcon { get; set; }
        public IconData? BadgeConnectingIcon { get; set; }
        public IconData? ConnectedIcon { get; set; }
        public IconData? ConnectingIcon { get; set; }
        public IconData? DisconnectedIcon { get; set; }
        public ImageData? NotificationImage { get; set; }
        public ImageData? QuickLaunchTileImage { get; set; }
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
