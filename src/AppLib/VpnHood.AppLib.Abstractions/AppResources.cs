using VpnHood.Core.Toolkit.Graphics;

namespace VpnHood.AppLib.Abstractions;

public class AppResources
{
    // Lazy: the ~14 MB IP-location db is materialized only on first .Value — i.e. when a country
    // split or location lookup actually runs (see VpnHoodApp / SplitCountryService), not at startup.
    // The null-vs-non-null check still tells "provided" from "not provided" without loading it.
    public Lazy<byte[]>? IpLocationZipData { get; set; }
    public byte[]? SpaZipData { get; set; }
    public VhSize WindowSize { get; set; } = new(400, 700);
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
        public VhColor? NavigationBarColor { get; set; }
        public VhColor? WindowBackgroundColor { get; set; }
        public VhColor? ProgressBarColor { get; set; }
    }

    public class AppIcons
    {
        // Lazy: the default bytes are pulled from the embedded resource only on first access, so an app
        // that never shows a given icon (or that the SPA overrides via SpaResourcesFactory) pays nothing
        // for it at startup. A caller-assigned value wins; setting null re-arms the default on next read.
        public IconData? BadgeConnectedIcon { get => field ??= new IconData(Resources.BadgeConnectedIcon); set; }
        public IconData? BadgeConnectingIcon { get => field ??= new IconData(Resources.BadgeConnectingIcon); set; }
        public IconData? SystemTrayConnectedIcon { get => field ??= new IconData(Resources.VpnConnectedIcon); set; }
        public IconData? SystemTrayConnectingIcon { get => field ??= new IconData(Resources.VpnConnectingIcon); set; }
        public IconData? SystemTrayDisconnectedIcon { get => field ??= new IconData(Resources.VpnDisconnectedIcon); set; }
    }

    public class IconData(byte[] data)
    {
        public byte[] Data { get; } = data;
    }
}