using System.Drawing;

namespace VpnHood.Client.App;

public class AppResource
{
    public byte[]? SpaZipData { get; set; }
    public Size WindowSize { get; set; } = new(400, 700);
    public AppStrings Strings { get; set; } = new();
    public AppColors Colors { get; set; } = new();
    public AppIcons Icons { get; set; } = new();

    public class AppStrings
    {
        public string AppName { get; set; } = Resource.AppName;
        public string Disconnect { get; set; } = Resource.Disconnect;
        public string Connect { get; set; } = Resource.Connect;
        public string Disconnected { get; set; } = Resource.Disconnected;
        public string Exit { get; set; } = Resource.Exit;
        public string Manage { get; set; } = Resource.Manage;
        public string MsgAccessKeyAdded { get; set; } = Resource.MsgAccessKeyAdded;
        public string MsgAccessKeyUpdated { get; set; } = Resource.MsgAccessKeyUpdated;
        public string MsgCantReadAccessKey { get; set; } = Resource.MsgCantReadAccessKey;
        public string MsgUnsupportedContent { get; set; } = Resource.MsgUnsupportedContent;
        public string Open { get; set; } = Resource.Open;
        public string OpenInBrowser { get; set; } = Resource.OpenInBrowser;
    }

    public class AppColors
    {
        public Color? NavigationBarColor { get; set; }
        public Color? WindowBackgroundColor { get; set; }
        public Color? ProgressBarColor { get; set; }
    }

    public class AppIcons
    {
        public IconData? BadgeConnectedIcon { get; set; } = new(Resource.BadgeConnectedIcon);
        public IconData? BadgeConnectingIcon { get; set; } = new(Resource.BadgeConnectingIcon);
        public IconData? SystemTrayConnectedIcon { get; set; } 
        public IconData? SystemTrayConnectingIcon { get; set; }
        public IconData? SystemTrayDisconnectedIcon { get; set; }
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
