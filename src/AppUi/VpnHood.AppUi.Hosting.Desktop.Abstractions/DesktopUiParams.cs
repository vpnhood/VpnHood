using VpnHood.AppLib.Api;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

// What a desktop host hands the UI it runs (IDesktopUi): everything a UI reads the app through.
public class DesktopUiParams
{
    // The app's API. Over loopback when the app runs in a service; the app's own controllers when it
    // runs in the UI's process.
    public required VpnHoodApi Api { get; init; }

    // The UI's own content, out of the store the head placed with it: a UI that draws its own
    // pictures and words reads them here. Extracted under the current user's cache when the app runs
    // in a service, whose storage the user may not read.
    public required IAssetProvider? UiAssetProvider { get; init; }

    // The app's API URL, followed as it moves: a web view UI loads the page from here, and again when
    // it changes.
    public required IDesktopApiUrlProvider ApiUrlProvider { get; init; }

    // A folder of the person's own the UI may keep files in: a web view's profile. The app's storage
    // is not theirs to write when the app runs in a service.
    public required string UiDataPath { get; init; }

    // Whether the run starts with the window closed, waiting in the UI's tray until it is asked for:
    // the entry an installer runs at sign-in. A UI with no tray to wait in ends at once.
    public required bool StartHidden { get; init; }

    // Whether the UI may keep a tray icon, in which a closed window waits until the tray's Exit; with
    // none, closing the window ends the UI. Not in a sandboxed app (the Store edition's), which Windows
    // refuses a tray icon.
    public required bool UseTray { get; init; }

    // Whether closing the window while the app's VPN is up only hides it, until the VPN is down: the app runs in
    // the UI's process, and the VPN only while the app does (the Windows plug-in's). A second launch shows it
    // again.
    public required bool KeepRunningWhileConnected { get; init; }

    // The OS's ways for links and the clipboard where the UI framework's own are refused, a sandboxed app's
    // (IDesktopShell); null: the framework's own.
    public required IDesktopShell? Shell { get; init; }

    // The access key this launch was given - a file opened with the app - which the UI imports once it
    // runs, as IDesktopUi.ImportAccessKey does a second launch's; null for a plain start.
    public string? AccessKey { get; init; }
}
