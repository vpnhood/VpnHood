using System.Reflection;

namespace VpnHood.AppUi.Hosting.Cli.Linux;

// Where a Linux install keeps things, and why they are not all one folder.
//
// The daemon runs as root and owns the storage folder: the settings a person edits by hand, the
// profiles, the log. The window and the commands run as whoever is logged in and may not write a
// byte of it, so the content the UI extracts for itself goes under that person's cache instead.
// The install's own places follow where the binary sits; a person's follow the app id, which no
// other app on the machine has and a Debug build does not share with a release.
public class LinuxCliPaths(string appId) : IAppCliPaths
{
    // The launcher script says its own name in this variable, because the binary cannot know it:
    // "vhclient" is what is on the PATH, and "VpnHoodClient" - the binary's name - is not.
    public const string LauncherNameVariable = "VH_LAUNCHER_NAME";

    // The installed binary's own name, which is the name of what the installer builds around it: the
    // systemd unit and the folder under /opt. Taken from
    // the entry assembly rather than from a constant so the two heads - VpnHoodClient and
    // VpnHoodConnect - need no constant of their own, and a fork that renames its assembly renames
    // all of it; not from the process, which "dotnet VpnHoodClient.dll" - a debugger's run - names dotnet.
    public string InstanceName { get; } =
        Assembly.GetEntryAssembly()?.GetName().Name ??
        throw new InvalidOperationException("The entry assembly has no name, so this install has none.");

    public string CommandName =>
        Environment.GetEnvironmentVariable(LauncherNameVariable) is { Length: > 0 } launcher
            ? launcher
            : InstanceName;

    // /opt/VpnHoodClient/<version>/VpnHoodClient -> /opt/VpnHoodClient/storage, which is what every
    // release of this package has used and where an advanced user's settings.json already is.
    public string StoragePath {
        get {
            var exePath = Environment.ProcessPath ??
                          throw new InvalidOperationException("The process path is unknown, so the storage folder cannot be found.");
            var versionDir = Path.GetDirectoryName(exePath) ??
                             throw new InvalidOperationException($"The executable has no folder: {exePath}");
            var installDir = Path.GetDirectoryName(versionDir) ??
                             throw new InvalidOperationException($"The version folder has no parent: {versionDir}");
            return Path.Combine(installDir, "storage");
        }
    }

    // Per user, under XDG's cache: the daemon's storage belongs to root and a desktop session
    // cannot write there.
    public string UiDataPath {
        get {
            var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrWhiteSpace(cacheHome))
                cacheHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");

            return Path.Combine(cacheHome, appId);
        }
    }

    // Under XDG's data rather than the UI's cache: settings and profiles are not something to lose.
    public string DevStoragePath {
        get {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome))
                dataHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

            return Path.Combine(dataHome, appId);
        }
    }
}
