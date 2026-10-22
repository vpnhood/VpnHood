namespace VpnHood.AppUi.Hosting.Desktop.Platform;

// Where an install keeps things, as the commands need to know it: the window's share
// (IDesktopUiPaths) and the daemon's. Two of these differ by platform and one by distribution: /opt
// on Linux, ProgramData on Windows. Nothing here is a constant, and nothing here is read through a
// static - a command is handed the one for the machine it runs on.
public interface IAppDesktopPaths : IDesktopUiPaths
{
    // The word a person types to run this again, which every hint prints: "vhclient", not the
    // binary's name, which on Linux is not on the PATH at all.
    string CommandName { get; }

    // Whether this process was started by that word rather than by the window's own executable, which
    // a desktop entry runs: run alone, the one prints its help and the other opens the window.
    bool IsStartedAsCommand { get; }

    // The daemon's: settings.json, the profiles, the log, which only it and administrators may open -
    // not the person at the window, whose commands ask the daemon instead.
    string StoragePath { get; }

    // The app's storage when a debugger runs it in the person's own process (the "dev" command), which
    // may not write the daemon's: the person's folder of the app, a Debug build's id being its own. A
    // platform whose UI folder is a cache keeps it elsewhere.
    string DevStoragePath => UiDataPath;
}
