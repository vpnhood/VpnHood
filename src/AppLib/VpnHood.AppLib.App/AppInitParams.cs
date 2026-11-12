namespace VpnHood.AppLib.App;

/// <summary>
/// Provides what a head hands its platform's <c>Init</c>, the same on every platform: who the app is, where the
/// platform keeps its files, and how to build its options once the platform has said where things are.
/// </summary>
/// <remarks>
/// The platform settles the context from it (<see cref="AppOptionsContext"/>), calls the head's factory, fills in its
/// own defaults for what the head left unset, and makes the app; a desktop platform takes its single-instance lock
/// first. A platform that needs more takes a derived class.
/// </remarks>
public class AppInitParams
{
    /// <summary>
    /// Gets the app's ID on the device, which the options carry, a desktop platform takes its single-instance lock on,
    /// and a Linux tunnel is tagged with.
    /// </summary>
    public required string AppId { get; init; }

    /// <summary>
    /// Gets the name people see, which the options carry, and which a desktop host names its service and its messages
    /// with in the processes that build no options.
    /// </summary>
    public required string AppName { get; init; }

    /// <summary>
    /// Gets the app's own version, beside its name, not the library's, which the options carry.
    /// </summary>
    public required Version AppVersion { get; init; }

    /// <summary>
    /// Gets the name of the app's folder under the platform's own place for app data, and of its device-only
    /// counterpart (<see cref="ResolveNoBackupStoragePath"/>), or <see langword="null"/> where a host lays its storage
    /// out itself, as the Linux service does.
    /// </summary>
    /// <remarks>
    /// Required on Android and iOS, whose platforms name the folders; the desktop platforms lay their storage out
    /// themselves and do not read it. Never change it for a head that has shipped: a new name is an empty folder, and
    /// every setting and profile stays behind in the old one.
    /// </remarks>
    public string? StorageFolderName { get; init; }

    /// <summary>
    /// Gets the head's options factory, which the platform calls once, in the process that runs the app, after it has
    /// settled the context.
    /// </summary>
    public required Func<AppOptionsContext, AppOptions> AppOptionsFactory { get; init; }

    /// <summary>
    /// Returns the app's folder under the system's local app data: <c>%LOCALAPPDATA%</c> on Windows, the app's files
    /// folder on Android, Documents on iOS.
    /// </summary>
    /// <returns>The folder's path, <see cref="StorageFolderName"/> in the local app data.</returns>
    /// <exception cref="InvalidOperationException">
    /// The system has no local app data folder, or <see cref="StorageFolderName"/> is <see langword="null"/>.
    /// </exception>
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

    /// <summary>
    /// Returns the app's device-only folder (<see cref="AppOptionsContext.NoBackupStoragePath"/>): the same name, under
    /// the folder the platform keeps out of backups.
    /// </summary>
    /// <param name="noBackupFolderPath">The platform's folder that is kept out of backups.</param>
    /// <returns>The folder's path, <see cref="StorageFolderName"/> in <paramref name="noBackupFolderPath"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="StorageFolderName"/> is <see langword="null"/>.
    /// </exception>
    public string ResolveNoBackupStoragePath(string noBackupFolderPath)
    {
        return Path.Combine(noBackupFolderPath, RequiredStorageFolderName);
    }

    private string RequiredStorageFolderName => StorageFolderName ?? throw new InvalidOperationException(
        $"This platform keeps the app's files in a folder the head names; {nameof(StorageFolderName)} is required.");
}
