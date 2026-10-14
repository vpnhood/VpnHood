using System.Globalization;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.App;

// What the engine itself shows: its own words and the taskbar badge. The window's look - its size, its
// colours, the chrome around it - is the UI's, which the engine never draws.
public class AppResources
{
    // Each word is resolved on read - the provider, or this library's own English - so a language
    // chosen while the app runs is the one shown.
    public IAppStrings Strings { get; }

    // The UI's words, pushed through ConfigParams.Strings; null, and every word is the resx's.
    public IStringProvider? StringProvider { get; set; }

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
        public string Manage => resources.GetString("MANAGE") ?? Resources.Manage;
        public string MsgAccessKeyAdded => resources.GetString("MSG_ACCESS_KEY_ADDED") ?? Resources.MsgAccessKeyAdded;
        public string MsgAccessKeyUpdated => resources.GetString("MSG_ACCESS_KEY_UPDATED") ?? Resources.MsgAccessKeyUpdated;
        public string MsgCantReadAccessKey => resources.GetString("MSG_CANT_READ_ACCESS_KEY") ?? Resources.MsgCantReadAccessKey;
        public string MsgUnsupportedContent => resources.GetString("MSG_UNSUPPORTED_CONTENT") ?? Resources.MsgUnsupportedContent;
        public string UpdateReadyToInstall => resources.GetString("UPDATE_READY_TO_INSTALL") ?? Resources.UpdateReadyToInstall;
    }

    // The OS chrome the app draws for itself: a taskbar badge. The tray's pictures are not here: the
    // tray draws the app's own icon, from its build. Every one always has an answer - what the head
    // assigned, or this library's own copy - so an app that supplies nothing still draws something
    // rather than nothing. A product replaces any of them at configure time by assigning; setting null
    // re-arms the default on the next read. Nothing here reads a file or knows where a product keeps
    // its artwork: that is the head's business, and this library must not reach out for it.
    public class AppIcons
    {
        public ReadOnlyMemory<byte>? BadgeConnectedIconData { get => field ??= Resources.BadgeConnectedIcon; set; }
        public ReadOnlyMemory<byte>? BadgeConnectingIconData { get => field ??= Resources.BadgeConnectingIcon; set; }
    }
}