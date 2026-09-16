using VpnHood.AppLib.Assets;
using VpnHood.Core.Toolkit.Graphics;

namespace VpnHood.AppLib.Abstractions;

public class AppResources
{
    public byte[]? SpaZipData { get; set; }

    // The Avalonia UI's browser build (VpnHood.App.AvaloniaUI.Browser), which the web server hands a
    // paired device in place of the SPA; null when the head ships none, and the SPA serves everyone.
    public byte[]? AvaloniaBrowserZipData { get; set; }

    public VhSize WindowSize { get; set; } = new(400, 700);

    public AppStrings Strings { get; set; } = new();
    public AppColors Colors { get; set; } = new();
    public AppIcons Icons { get; set; } = new();

    public class AppStrings
    {
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
        // Files, not bytes in an assembly. These are OS-chrome icons - a tray, a taskbar badge - so
        // they belong with the app's other content (VpnHood.AppLib.Assets), which every head's build
        // places the way its platform reads files. Keeping them embedded cost 612 KB in a library
        // every UI links, and Android packs each assembly once per CPU architecture, so the same
        // bytes shipped three times. Read lazily: a head that never draws a tray pays nothing, and a
        // build with no content folder - a test, or the browser head, which has no chrome to draw -
        // reads null, which is what every caller already checks for. A caller-assigned value wins
        // (SpaResourcesFactory hands over the branded icons from spa.zip); setting null re-arms the
        // default on the next read.
        public byte[]? BadgeConnectedIconData { get => field ??= ReadIcon("BadgeConnected.ico"); set; }
        public byte[]? BadgeConnectingIconData { get => field ??= ReadIcon("BadgeConnecting.ico"); set; }
        public byte[]? SystemTrayConnectedIconData { get => field ??= ReadIcon("VpnConnected.ico"); set; }
        public byte[]? SystemTrayConnectingIconData { get => field ??= ReadIcon("VpnConnecting.ico"); set; }
        public byte[]? SystemTrayDisconnectedIconData { get => field ??= ReadIcon("VpnDisconnected.ico"); set; }

        private static byte[]? ReadIcon(string fileName)
        {
            if (!AppContent.TryGetFolderPath(out var folderPath))
                return null;

            var filePath = Path.Combine(folderPath, IconsFolderName, fileName);
            return File.Exists(filePath) ? File.ReadAllBytes(filePath) : null;
        }

        private const string IconsFolderName = "icons";
    }
}