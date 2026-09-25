using VpnHood.AppLib.Api;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Abstractions;

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

    // Where the app's web host serves its page and its API: a web view UI loads the page from here.
    public required Uri WebUrl { get; init; }

    // A folder of the person's own the UI may keep files in: a web view's profile. The app's storage
    // is not theirs to write when the app runs in a service.
    public required string UiDataPath { get; init; }

    // Whether closing the window ends the UI: true where no tray keeps it (Linux); where one does,
    // closing only hides the window.
    public required bool ExitOnClose { get; init; }

    // Whether the window stays closed at the start, the UI waiting in the tray until it is asked for
    // (Windows, started at logon). Never where closing ends the UI: there would be nothing to ask.
    public required bool StartHidden { get; init; }
}
