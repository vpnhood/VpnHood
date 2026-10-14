namespace VpnHood.AppLib.App;

// What a head hands its platform's Init, the same on every platform: who the app is, where the
// platform keeps its files, and how to build its options once the platform has said where things
// are (AppOptionsContext). The platform does the rest in one order: its single-instance lock and
// cleanup on a desktop, the context, the head's factory, its own defaults for what the head left
// unset, then the app. A platform that needs more takes a subclass (IosInitParams).
public class AppInitParams
{
    // The app's identity on the device: AppOptions carries it, a desktop platform takes its
    // single-instance lock on it, and a Linux tun is tagged with it.
    public required string AppId { get; init; }

    // The name people see (AppConstants.AppName). AppOptions carries it, and a desktop host names
    // its service and its message with it in the processes that never build the options.
    public required string AppName { get; init; }

    // The app's version, Version.Parse(AppConstants.AppVersion): the Version it states beside its name,
    // not the library's, so a version that is not numbers stops the app at its start. AppOptions
    // carries it.
    public required Version AppVersion { get; init; }

    // The folder, under the platform's own place for app data, that holds the settings and the
    // profiles; one of the same name under its device-only place holds the sign-in session, the log
    // and the caches (ResolveNoBackupStoragePath). Required where the platform names such a folder
    // (Android, iOS, a Windows desktop app); null where a host lays storage out itself (the Linux
    // daemon). Never changed for a head that has shipped: a new name is an empty folder, and every
    // setting and profile left behind in the old one.
    public string? StorageFolderName { get; init; }

    // Called once, in the process that runs the app, after the platform has resolved the context.
    public required Func<AppOptionsContext, AppOptions> AppOptionsFactory { get; init; }

    // Where a platform that names folders keeps them: the OS's local app data - %LOCALAPPDATA% on
    // Windows, the app's files folder on Android, Documents on iOS.
    public string ResolveStoragePath()
    {
        // GetFolderPath answers an empty string for a folder that does not exist, which Path.Combine
        // would turn into a path relative to wherever the process happens to run
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
            throw new InvalidOperationException(
                $"This platform has no {nameof(Environment.SpecialFolder.LocalApplicationData)} folder to keep the app's files in.");

        return Path.Combine(localAppData, RequiredStorageFolderName);
    }

    // Its device-only counterpart (AppOptionsContext.NoBackupStoragePath): the same name, under the
    // folder the platform keeps out of backups.
    public string ResolveNoBackupStoragePath(string noBackupFolderPath)
    {
        return Path.Combine(noBackupFolderPath, RequiredStorageFolderName);
    }

    private string RequiredStorageFolderName => StorageFolderName ?? throw new InvalidOperationException(
        $"This platform keeps the app's files in a folder the head names; {nameof(StorageFolderName)} is required.");
}
