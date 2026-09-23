namespace VpnHood.AppUi.Hosting.Cli;

// Where an install keeps things, as the commands need to know it. Two of these differ by platform
// and one by distribution: /opt on Linux, ProgramData on Windows, a package folder in a Store
// build. Nothing here is a constant, and nothing here is read through a static - a command is
// handed the one for the machine it runs on.
public interface IAppCliPaths
{
    // The install's own name: the systemd unit, the folder under /opt, the cache under a home.
    // "VpnHoodClient" or "VpnHoodConnect", however this platform spells it.
    string InstanceName { get; }

    // The word a person types to run this again, which every hint prints: "vhclient", not the
    // binary's name, which on Linux is not on the PATH at all.
    string CommandName { get; }

    // The daemon's, and root's: settings.json, the profiles, the log.
    string StoragePath { get; }

    // The UI's own extracted content, per user, because the daemon's copy is in a folder a session
    // cannot read.
    string UiContentCachePath { get; }

    // The address the running daemon publishes and everything else reads. In the storage folder
    // because that is the one path all three processes agree on without being told.
    string DaemonInfoFilePath => Path.Combine(StoragePath, "daemon.json");
}
