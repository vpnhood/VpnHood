namespace VpnHood.AppUi.Hosting.Desktop.Platform;

// Where the window keeps things: the window's share of an install's paths (IAppDesktopPaths), and all
// a host without a service hands it (IInProcessDesktopPlatform).
public interface IDesktopUiPaths
{
    // The install's own name: the service, the folder it keeps its storage in, a person's folder of
    // the UI's, the window's single instance. "VpnHoodClient" or "VpnHoodConnect", however this
    // platform spells it.
    string InstanceName { get; }

    // The UI's own folder, per person, because the daemon's storage is not theirs to write: the UI's
    // extracted content, a web view's profile.
    string UiDataPath { get; }

    // The UI's own extracted content, under the UI's folder, named apart from the app's own copies
    // (assets/ui), which share that folder when the app runs in the window's process.
    string UiContentCachePath => Path.Combine(UiDataPath, "assets", "window");
}
