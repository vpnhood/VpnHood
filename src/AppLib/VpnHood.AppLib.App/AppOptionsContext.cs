using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Storages;

namespace VpnHood.AppLib.App;

/// <summary>
/// Provides what the platform has settled by the time it calls the head's options factory
/// (<see cref="AppInitParams.AppOptionsFactory"/>), so every provider the head makes is made with it: an account
/// provider, say, reads its saved session from the storage when it is made.
/// </summary>
public class AppOptionsContext
{
    /// <summary>Gets the app's ID on the device (<see cref="AppInitParams.AppId"/>).</summary>
    public required string AppId { get; init; }

    /// <summary>Gets the name people see (<see cref="AppInitParams.AppName"/>).</summary>
    public required string AppName { get; init; }

    /// <summary>
    /// Gets the app's version, as the head passed it (<see cref="AppInitParams.AppVersion"/>), which the app shows and
    /// compares its updates with; the servers get the library's.
    /// </summary>
    public required Version AppVersion { get; init; }

    /// <summary>Gets the folder that holds the settings and the VPN profiles, which travel with a backup.</summary>
    public required string StoragePath { get; init; }

    /// <summary>
    /// Gets the folder for what must not travel with a backup: the sign-in session, the log and the caches.
    /// </summary>
    /// <remarks>
    /// The platform chooses it: Android's no-backup folder; on iOS, a folder the app asks the system to leave out of
    /// backups, as far as it can; on a desktop, the storage folder itself.
    /// </remarks>
    public required string NoBackupStoragePath { get; init; }

    /// <summary>
    /// Gets where the files under the two folders that the app cannot make again are kept, such as the settings, the
    /// profiles with their keys, the install's ID and the sign-in session. The default is plain files.
    /// </summary>
    /// <remarks>
    /// On an Apple TV, which empties every folder an app may write when the device runs low on storage, they are kept
    /// in the app's defaults. What can be fetched or made again stays in plain files everywhere.
    /// </remarks>
    public IStorageProvider StorageProvider { get; init; } = new FileSystemStorageProvider();

    /// <summary>
    /// Gets a storage over <see cref="StoragePath"/> through <see cref="StorageProvider"/>, a new one at each read.
    /// </summary>
    public Storage Storage => new(StorageProvider, StoragePath);

    /// <summary>
    /// Gets a storage over <see cref="NoBackupStoragePath"/> through <see cref="StorageProvider"/>, a new one at each
    /// read; a provider of the head that keeps a file the app cannot make again, such as the sign-in session, takes its
    /// folder from here.
    /// </summary>
    public Storage NoBackupStorage => new(StorageProvider, NoBackupStoragePath);

    /// <summary>
    /// Gets the files the build's asset packages placed with the app, such as the IP-location database, the UI's store
    /// and the browser page, read the platform's way: from the folder beside the binary on a desktop and in an iOS
    /// bundle, from the APK's assets on Android.
    /// </summary>
    public required IAssetProvider PackagedAssetProvider { get; init; }
}
