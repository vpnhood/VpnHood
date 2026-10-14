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
    // the entry an installer runs at sign-in. A UI with no tray to wait in ends at once. Whether
    // closing the window ends the UI is the UI's own to decide, by its tray.
    public required bool StartHidden { get; init; }
}
