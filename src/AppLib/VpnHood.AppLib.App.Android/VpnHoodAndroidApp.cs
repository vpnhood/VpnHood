using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Android;

// The app on Android. The OS starts the head's Application in every process of the package - the
// VPN service's and the Quick Settings tile's too - and only the app's own process may build
// anything: so the init params come as a factory, called only there, and a head's configuration
// is never read where it is not needed.

/// <summary>Provides the app on Android, started in the app's own process only.</summary>
/// <remarks>
/// Android starts the head's application in every process of the package, the VPN service's and the Quick Settings
/// tile's too; only the app's own process builds anything, so the init params come as a factory, called only there.
/// </remarks>
public class VpnHoodAndroidApp : Singleton<VpnHoodAndroidApp>
{
    /// <summary>
    /// Starts the app, once, in the app's own process; in another process of the package, or a second time, it starts
    /// nothing.
    /// </summary>
    /// <param name="initParamsFactory">Makes the head's init params, called only where the app starts.</param>
    /// <returns>The app's Android host.</returns>
    /// <remarks>It throws what the app's start throws, as below.</remarks>
    /// <exception cref="Exception">An ad provider names countries both to include and to exclude.</exception>
    /// <exception cref="OverflowException">A duration in a built-in access key is out of range.</exception>
    /// <exception cref="InvalidDataException">A built-in access key decodes to JSON <c>null</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">A built-in access key holds no valid access key.</exception>
    /// <exception cref="NotSupportedException">A built-in access key's version is not one the app reads.</exception>
    /// <exception cref="FormatException">
    /// A built-in access key is no valid Base64, or an endpoint or a duration in it is not valid.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The process's or the package's identity cannot be read; or, in the app's own process, Android names no cache folder,
    /// the init params name no storage folder, the platform has no local app data folder, Android names no folder kept out
    /// of backups, or a debug build's storage check fails.
    /// </exception>
    public static VpnHoodAndroidApp Init(Func<AppInitParams> initParamsFactory)
    {
        if (IsInit)
            return Instance;

        AndroidEnvironment.UnhandledExceptionRaiser += OnUnhandledExceptionRaiser;

        // every process of the package logs to logcat, through its console, and to a debugger; the
        // VPN service's own process gets its log file from its host (VpnServiceHost). Without the
        // time, which logcat stamps on each line
        VhLogger.AddProvider(new ConsoleLoggerProvider(includeTime: false));
        VhLogger.AddProvider(new TraceLoggerProvider(includeTime: false));

        // do not init again, or in any process but the app's own: the VPN service's, the tile's, or any
        // other a library adds
        if (VpnHoodApp.IsInit || !AndroidDevice.IsMainProcess)
            return new VpnHoodAndroidApp();

        var initParams = initParamsFactory();
        var storagePath = initParams.ResolveStoragePath();
        var noBackupStoragePath = initParams.ResolveNoBackupStoragePath(
            Application.Context.NoBackupFilesDir?.AbsolutePath ??
            throw new InvalidOperationException("Android named no no-backup folder for the app."));

        // before the head's factory, whose providers read the sign-in session at once
#pragma warning disable CS0618 // Type or member is obsolete
        AppStorageMigration.MoveToNoBackup(storagePath, noBackupStoragePath);
#pragma warning restore CS0618 // Type or member is obsolete
        var context = new AppOptionsContext {
            AppId = initParams.AppId,
            AppName = initParams.AppName,
            AppVersion = initParams.AppVersion,
            StoragePath = storagePath,
            NoBackupStoragePath = noBackupStoragePath,
            PackagedAssetProvider = new AndroidAssetProvider(Application.Context)
        };

        var options = initParams.AppOptionsFactory(context);
        if (options.IsDebugMode)
            VerifyStoragePath(storagePath);
        options.DeviceUiProvider ??= new AndroidDeviceUiProvider();
        options.CultureProvider ??= AndroidAppCultureProvider.CreateIfSupported();
        options.DeviceId ??= AndroidUtils.GetDeviceId(Application.Context); //this will be hashed using AppId

        var vpnHoodDevice = AndroidDevice.Create();
        VpnHoodApp.Init(vpnHoodDevice, options);
        return new VpnHoodAndroidApp();
    }

    // A Debug build's check that .NET still maps the app's local data to its files folder: a runtime
    // that moved it would start every updated install empty, as Xamarin's files/.local/share once
    // misled the backup rules.
    private static void VerifyStoragePath(string storagePath)
    {
        var filesPath = Application.Context.FilesDir?.AbsolutePath ??
                        throw new InvalidOperationException("Android named no files folder for the app.");
        if (Path.GetDirectoryName(storagePath) != filesPath)
            throw new InvalidOperationException(
                $"The app's storage is not in its files folder. StoragePath: {storagePath}, FilesDir: {filesPath}");
    }

    private static void OnUnhandledExceptionRaiser(object? sender, RaiseThrowableEventArgs args)
    {
        // Log the error to your analytics service here
        var exception = args.Exception;
        VhLogger.Instance.LogError(exception, "Unhandled exception in Android environment");
    }

    /// <inheritdoc/>
    /// <remarks>It disposes the app.</remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (VpnHoodApp.IsInit)
                VpnHoodApp.Instance.Dispose();
        }

        base.Dispose(disposing);
    }
}