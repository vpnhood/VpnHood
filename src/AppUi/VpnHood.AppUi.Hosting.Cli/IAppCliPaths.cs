namespace VpnHood.AppUi.Hosting.Cli;

// Where an install keeps things, as the commands need to know it. Two of these differ by platform
// and one by distribution: /opt on Linux, ProgramData on Windows, a package folder in a Store
// build. Nothing here is a constant, and nothing here is read through a static - a command is
// handed the one for the machine it runs on.
public interface IAppCliPaths
{
    // The install's own name: the service, the folder it keeps its storage in, a person's folder of
    // the UI's. "VpnHoodClient" or "VpnHoodConnect", however this platform spells it.
    string InstanceName { get; }

    // The word a person types to run this again, which every hint prints: "vhclient", not the
    // binary's name, which on Linux is not on the PATH at all.
    string CommandName { get; }

    // The daemon's: settings.json, the profiles, the log. The person at the window may read it and
    // may not write it.
    string StoragePath { get; }

    // The UI's own folder, per person, because the daemon's storage is not theirs to write: the UI's
    // extracted content, a web view's profile.
    string UiDataPath { get; }

    // The UI's own extracted content, under the UI's folder, named apart from the app's own copies
    // (assets/ui), which share that folder when a debugger runs the app there (DevStoragePath).
    string UiContentCachePath => Path.Combine(UiDataPath, "assets", "window");

    // The app's storage when a debugger runs it in the person's own process (the "dev" command), which
    // may not write the daemon's: the person's folder of the app, a Debug build's id being its own. A
    // platform whose UI folder is a cache keeps it elsewhere.
    string DevStoragePath => UiDataPath;

    // The address the running daemon publishes and everything else reads. In the storage folder
    // because that is the one path all three processes agree on without being told.
    string DaemonInfoFilePath => Path.Combine(StoragePath, "daemon.json");
}
