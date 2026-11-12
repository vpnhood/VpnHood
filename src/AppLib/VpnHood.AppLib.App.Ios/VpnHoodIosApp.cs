using Foundation;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Ios;
using VpnHood.Core.Client.Devices.Ios.Utils;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Storages;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Ios;

// The app on iOS, from the same init params as every platform plus the two iOS facts
// (IosInitParams). The Network Extension is a binary of its own and never runs this.
/// <summary>
/// Provides the app on iOS, from the init params of every platform and the two iOS facts. The Network Extension is a
/// binary of its own and never runs it.
/// </summary>
public class VpnHoodIosApp : Singleton<VpnHoodIosApp>
{
    /// <summary>Starts the app, once; a second call gives the app started already.</summary>
    /// <param name="initParams">The init params.</param>
    /// <returns>The app.</returns>
    /// <remarks>
    /// It keeps what must not travel with a backup in a folder beside the storage, which it marks for iOS not to back up; a
    /// mark that cannot be set is logged. On tvOS, which may empty every folder an app writes, it keeps the storage in the
    /// app's defaults. An ad provider that names countries both to include and to exclude fails the start.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The init params' app name is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">A duration in a built-in access key is out of range.</exception>
    /// <exception cref="InvalidDataException">A built-in access key decodes to JSON <c>null</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">A built-in access key holds no valid access key.</exception>
    /// <exception cref="NotSupportedException">A built-in access key's version is not one the app reads.</exception>
    /// <exception cref="FormatException">
    /// A built-in access key is no valid Base64, or an endpoint or a duration in it is not valid.
    /// </exception>
    /// <exception cref="ArgumentException">The init params' app name is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The init params name no storage folder, or an App Group without a container, as where the entitlements lack it; the
    /// platform has no local app data folder; or the app's storage has no parent folder.
    /// </exception>
    public static VpnHoodIosApp Init(IosInitParams initParams)
    {
        // do not init again
        if (IsInit)
            return Instance;

        if (VpnHoodApp.IsInit)
            return new VpnHoodIosApp();

        // the app's process has a readable stdout, so its console is a sink, and a debugger is
        VhLogger.AddProvider(new ConsoleLoggerProvider());
        VhLogger.AddProvider(new TraceLoggerProvider());

        // the device-only folder beside the storage, carrying iOS's do-not-back-up flag, takes what must
        // not travel before the head's factory, whose providers read the sign-in session at once
        var storagePath = initParams.ResolveStoragePath();
        var storageParentPath = Path.GetDirectoryName(storagePath) ??
                                throw new InvalidOperationException("The app's storage has no parent folder.");
        var noBackupStoragePath = initParams.ResolveNoBackupStoragePath(Path.Combine(storageParentPath, "no-backup"));
        IosNoBackupFolder.Create(noBackupStoragePath);
#pragma warning disable CS0618 // Type or member is obsolete
        AppStorageMigration.MoveToNoBackup(storagePath, noBackupStoragePath);
#pragma warning restore CS0618 // Type or member is obsolete

        // the bundle is a folder, and the asset packages placed their files in it
        var context = new AppOptionsContext {
            AppId = initParams.AppId,
            AppName = initParams.AppName,
            AppVersion = initParams.AppVersion,
            StoragePath = storagePath,
            NoBackupStoragePath = noBackupStoragePath,
            // tvOS empties both folders - every folder an app may write is a cache there - when the device
            // runs low on storage, and keeps only the app's defaults
            StorageProvider = OperatingSystem.IsTvOS()
                ? new UserDefaultsStorageProvider(storageParentPath)
                : new FileSystemStorageProvider(),
            PackagedAssetProvider = new FolderAssetProvider(AppContext.BaseDirectory)
        };

        var options = initParams.AppOptionsFactory(context);
        options.DeviceUiProvider ??= new IosDeviceUiProvider();
        options.CultureProvider ??= IosAppCultureProvider.CreateIfSupported();

        VpnHoodApp.Init(CreateDevice(initParams, options.AppName), options);
        return new VpnHoodIosApp();
    }

    // Resolved here - on the main thread, after iOS has set up the sandbox - so the App Group's
    // container path, the app's and the extension's IPC folder, is stable for the whole session.
    private static IosDevice CreateDevice(IosInitParams initParams, string appName)
    {
        // the binding declares it non-null, but iOS answers nil for a group the entitlements lack
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        var groupContainerPath = NSFileManager.DefaultManager.GetContainerUrl(initParams.AppGroupId)?.Path
            ?? throw new InvalidOperationException(
                $"The App Group {initParams.AppGroupId} has no container: add it to the entitlements and the " +
                "provisioning profiles of both the app and its extension, which share the VPN service's folder there.");
        VhLogger.Instance.LogInformation("GetContainerUrl({AppGroupId}) = {Path}",
            initParams.AppGroupId, groupContainerPath);

        // an Apple TV lets an app write only the Caches of a shared container: a device refuses the rest,
        // though the simulator allows it
        var sharedContainerPath = OperatingSystem.IsTvOS()
            ? Path.Combine(groupContainerPath, "Library", "Caches")
            : groupContainerPath;

        return new IosDevice(
            providerBundleId: initParams.ProviderBundleId,
            sharedContainerPath: sharedContainerPath,
            localizedDescription: appName);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (VpnHoodApp.IsInit)
                VpnHoodApp.Instance.Dispose();
        }

        base.Dispose(disposing);
    }
}
