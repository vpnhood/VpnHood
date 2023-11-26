using System.Drawing;

namespace VpnHood.Client.App;

public class AppResources
{
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
        public byte[]? AppIcon { get; set; }
        public byte[]? BadgeConnectedIcon { get; set; }
        public byte[]? BadgeConnectingIcon { get; set; }
        public byte[]? ConnectedIcon { get; set; }
        public byte[]? ConnectingIcon { get; set; }
        public byte[]? DisconnectedIcon { get; set; }
        public byte[]? NotificationImage { get; set; }
    }
}