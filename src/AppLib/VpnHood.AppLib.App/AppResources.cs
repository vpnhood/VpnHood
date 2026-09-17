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

    // The OS chrome the app draws for itself: a tray icon, a taskbar badge. Every one always has an
    // answer - what the head assigned, or this library's own copy - so an app that supplies nothing
    // still draws something rather than nothing. A product replaces any of them at configure time by
    // assigning; setting null re-arms the default on the next read. Nothing here reads a file or
    // knows where a product keeps its artwork: that is the head's business, and this library must
    // not reach out for it.
    public class AppIcons
    {
        public ReadOnlyMemory<byte>? BadgeConnectedIconData { get => field ??= Resources.BadgeConnectedIcon; set; }
        public ReadOnlyMemory<byte>? BadgeConnectingIconData { get => field ??= Resources.BadgeConnectingIcon; set; }
        public ReadOnlyMemory<byte>? SystemTrayConnectedIconData { get => field ??= Resources.VpnConnectedIcon; set; }
        public ReadOnlyMemory<byte>? SystemTrayConnectingIconData { get => field ??= Resources.VpnConnectingIcon; set; }
        public ReadOnlyMemory<byte>? SystemTrayDisconnectedIconData { get => field ??= Resources.VpnDisconnectedIcon; set; }
    }
}