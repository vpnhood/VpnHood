using VpnHood.AppLib.Api;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>Represents what a desktop host hands the UI it runs (<see cref="IDesktopUi"/>): everything a UI reads the app through.</summary>
public class DesktopUiParams
{
    /// <summary>
    /// Gets the app's API, over loopback HTTP: to the service's web host, or to the app's own where it runs in the UI's
    /// process.
    /// </summary>
    public required VpnHoodApi Api { get; init; }

    /// <summary>
    /// Gets the UI's own content, its pictures and its words, from the store the head placed with it, or
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>Where the app runs in a service, whose storage the person may not read, it is extracted under the person's cache.</remarks>
    public required IAssetProvider? UiAssetProvider { get; init; }

    /// <summary>Gets the app's API address, followed as it moves: a web view UI loads its page from it, and again when it changes.</summary>
    public required IDesktopApiUrlProvider ApiUrlProvider { get; init; }

    /// <summary>
    /// Gets a folder of the person's own the UI may keep files in, as a web view's profile; the app's storage is not theirs
    /// to write where the app runs in a service.
    /// </summary>
    public required string UiDataPath { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run starts with the window closed, waiting in the UI's tray until it is asked
    /// for, as the entry an installer runs at sign-in does. A UI with no tray to wait in ends the run, unless a launch has
    /// asked for the window.
    /// </summary>
    public required bool StartHidden { get; init; }

    /// <summary>
    /// Gets a value indicating whether the UI may keep a tray icon, where a closed window waits until the tray's Exit;
    /// without one, closing the window ends the UI, but for <see cref="KeepRunningWhileConnected"/>.
    /// </summary>
    /// <remarks>It is off where the platform keeps the app from a tray icon, as for the Store edition.</remarks>
    public required bool UseTray { get; init; }

    /// <summary>
    /// Gets a value indicating whether, where no tray is shown, closing the window only hides it, and, while it stays
    /// hidden, the run ends once the app's VPN can no longer be disconnected; for a host whose app runs in the UI's
    /// process, as the Windows plug-in's.
    /// </summary>
    /// <remarks>A second launch shows the window again.</remarks>
    public required bool KeepRunningWhileConnected { get; init; }

    /// <summary>
    /// Gets the system's ways for links, the clipboard and the taskbar button's badge where the UI framework's own or the
    /// host's are refused, as in a sandboxed app; or <see langword="null"/> for the framework's and the host's own.
    /// </summary>
    public required IDesktopShell? Shell { get; init; }

    /// <summary>
    /// Gets the access key this launch was given, as a file opened with the app, which the UI imports once it runs; or
    /// <see langword="null"/> for a plain start.
    /// </summary>
    public string? AccessKey { get; init; }

    /// <summary>
    /// Gets a value indicating whether this launch is a Quick Launch, the Store edition's own entry in Start, on which the
    /// UI connects once it runs.
    /// </summary>
    public bool IsQuickLaunch { get; init; }
}
