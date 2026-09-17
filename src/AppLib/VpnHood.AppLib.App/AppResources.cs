using VpnHood.AppLib.Assets;
using VpnHood.AppLib.Api.App;
using VpnHood.Core.Toolkit.Graphics;

namespace VpnHood.AppLib;

public class AppResources
{
    public byte[]? SpaZipData { get; set; }

    // The Avalonia UI's browser build (VpnHood.App.AvaloniaUI.Browser), which the web server hands a
    // paired device in place of the SPA; null when the head ships none, and the SPA serves everyone.
    public byte[]? AvaloniaBrowserZipData { get; set; }

    public VhSize WindowSize { get; set; } = new(400, 700);

    public AppStrings Strings { get; set; } = CreateDefaultStrings();
    public AppColors Colors { get; set; } = new();
    public AppIcons Icons { get; set; } = new();

    // The shape is the contract's (a UI replaces these at configure time); the words are this
    // library's, from its own resources, so a head that ships no UI still has them.
    private static AppStrings CreateDefaultStrings()
    {
        return new AppStrings {
            Disconnect = Resources.Disconnect,
            Connect = Resources.Connect,
            Disconnected = Resources.Disconnected,
            Exit = Resources.Exit,
            Manage = Resources.Manage,
            MsgAccessKeyAdded = Resources.MsgAccessKeyAdded,
            MsgAccessKeyUpdated = Resources.MsgAccessKeyUpdated,
            MsgCantReadAccessKey = Resources.MsgCantReadAccessKey,
            MsgUnsupportedContent = Resources.MsgUnsupportedContent,
            Open = Resources.Open,
            OpenInBrowser = Resources.OpenInBrowser
        };
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