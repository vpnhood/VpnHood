namespace VpnHood.AppUi.Hosting.Cli.Linux;

// Where a Linux install keeps things, and why they are not all one folder.
//
// The daemon runs as root and owns the storage folder: the settings a person edits by hand, the
// profiles, the log. The window and the commands run as whoever is logged in and may not write a
// byte of it, so the content the UI extracts for itself goes under that person's cache instead.
// Everything here is derived from two facts - where the binary sits and who is running it - so
// the Client and Connect heads each get their own answers without being told which they are.
public class LinuxCliPaths : IAppCliPaths
{
    // The launcher script says its own name in this variable, because the binary cannot know it:
    // "vhclient" is what is on the PATH, and "VpnHoodClient" - the binary's name - is not.
    public const string LauncherNameVariable = "VH_LAUNCHER_NAME";

    // The installed binary's own name, which is the name of everything built around it: the systemd
    // unit the installer writes, the folder under /opt, the cache under a person's home. Taken from
    // the process rather than from a constant so the two heads - VpnHoodClient and VpnHoodConnect -
    // need no constant of their own, and a fork that renames its assembly renames all of it.
    public string InstanceName { get; } =
        Path.GetFileNameWithoutExtension(Environment.ProcessPath) ??
        throw new InvalidOperationException("The process path is unknown, so this install has no name.");

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

    // Per user, under XDG's cache: the daemon's copy lives in root's storage and a desktop session
    // cannot write there.
    public string UiContentCachePath {
        get {
            var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrWhiteSpace(cacheHome))
                cacheHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");

            return Path.Combine(cacheHome, InstanceName, "assets", "ui");
        }
    }
}
