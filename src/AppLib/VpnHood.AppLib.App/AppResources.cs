using System.Globalization;
using VpnHood.AppLib.Abstractions;
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

    // Each word is resolved on read - the provider, or this library's own English - so a language
    // chosen while the app runs is the one shown.
    public IAppStrings Strings { get; }

    // The UI's words, pushed through ConfigParams.Strings; null, and every word is the resx's.
    public IStringProvider? StringProvider { get; set; }

    public AppColors Colors { get; set; } = new();
    public AppIcons Icons { get; set; } = new();

    public AppResources()
    {
        Strings = new AppStrings(this);
    }

    private string? GetString(string key)
    {
        // the app's language, not this thread's: VpnHoodApp writes it on the thread that initializes it
        var culture = CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture;
        return StringProvider?.GetString(culture, key);
    }

    // Each word is looked up when read: the provider, then Resources.resx.
    internal class AppStrings(AppResources resources) : IAppStrings
    {
        public string Disconnect => resources.GetString("DISCONNECT") ?? Resources.Disconnect;
        public string Connect => resources.GetString("CONNECT") ?? Resources.Connect;
        public string Disconnected => resources.GetString("DISCONNECTED") ?? Resources.Disconnected;
        public string Exit => resources.GetString("EXIT") ?? Resources.Exit;
        public string Manage => resources.GetString("MANAGE") ?? Resources.Manage;
        public string MsgAccessKeyAdded => resources.GetString("MSG_ACCESS_KEY_ADDED") ?? Resources.MsgAccessKeyAdded;
        public string MsgAccessKeyUpdated => resources.GetString("MSG_ACCESS_KEY_UPDATED") ?? Resources.MsgAccessKeyUpdated;
        public string MsgCantReadAccessKey => resources.GetString("MSG_CANT_READ_ACCESS_KEY") ?? Resources.MsgCantReadAccessKey;
        public string MsgUnsupportedContent => resources.GetString("MSG_UNSUPPORTED_CONTENT") ?? Resources.MsgUnsupportedContent;
        public string Open => resources.GetString("OPEN") ?? Resources.Open;
        public string OpenInBrowser => resources.GetString("OPEN_IN_BROWSER") ?? Resources.OpenInBrowser;
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