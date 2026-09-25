using VpnHood.AppLib.Api;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Cli;

// What the window's host hands the platform's tray (CliPlatform.CreateTray): the app it shows, the
// look it draws with, and the two things only the window's side can do.
public class DesktopTrayParams
{
    public required VpnHoodApi Api { get; init; }

    // The UI's store, whose branding holds the tray's icons.
    public required IAssetProvider? UiAssets { get; init; }

    // The window to the front.
    public required Func<CancellationToken, Task> ShowWindow { get; init; }

    // The UI ends: the window closes and this process goes; the service runs on.
    public required Action Exit { get; init; }
}
