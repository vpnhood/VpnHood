using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.WindowsTun;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.WindowsTun;

// The app as Windows runs it: one per app id on the machine, in a LocalSystem service. Init takes the
// single-instance lock before anything is built - a second daemon, started by hand while the service
// runs, touches nothing, not even the head's options - then starts the app. Stopping it is disposing
// it, and asynchronously, so the tunnel comes down before the process ends. The storage path is the
// host's (the service's folder under ProgramData), so the init params name no folder here. What a
// window shows of it - the tray, the window itself - runs in the person's own process, over the API;
// the VPN service runs in a process of its own too, the VPN host, which the service starts at a connect.
public class VpnHoodWindowsApp : Singleton<VpnHoodWindowsApp>, IAsyncDisposable
{
    private static IDisposable? _singleInstanceLock;
    private readonly VpnHoodApp _app;
    private bool _disposed;

    private VpnHoodWindowsApp(AppOptions appOptions, IDevice device)
    {
        _app = VpnHoodApp.Init(device, appOptions);
    }

    // device: a head's own, where the head runs the VPN service in its one process - a MAUI app, which has no daemon,
    // hands VpnServices.Host.WindowsTun's WindowsInProcessDevice on the same storage; null for the daemon, whose device
    // starts the VPN host (paths.VpnHostPath). The app owns it from here; a started app takes no other.
    public static VpnHoodWindowsApp Init(AppInitParams initParams, VpnHoodWindowsAppPaths paths, IDevice? device = null)
    {
        // this process already holds the lock; taking it again would call itself another instance
        if (IsInit)
            return device is null
                ? Instance
                : throw new InvalidOperationException("The app has started already, with its device: it takes no other.");

        _singleInstanceLock = paths.LockFolderPath is null
            ? ServiceInstanceLock.Take(initParams.AppId)
            : InstanceLockFile.Take(paths.LockFolderPath, initParams.AppId);

        Directory.CreateDirectory(paths.StoragePath);
        var context = new AppOptionsContext {
            AppId = initParams.AppId,
            AppName = initParams.AppName,
            AppVersion = initParams.AppVersion,
            StoragePath = paths.StoragePath,
            NoBackupStoragePath = paths.StoragePath, // nothing backs up a desktop app's data
            PackagedAssetProvider = new FolderAssetProvider(AppContext.BaseDirectory)
        };

        var appOptions = initParams.AppOptionsFactory(context);
        appOptions.DeviceUiProvider ??= new WindowsDeviceUiProvider();
        appOptions.EventWatcherInterval ??= TimeSpan.FromSeconds(1);

        // No DeviceId: the service's account is every machine's same S-1-5-18, so the install's own id
        // serves (VpnHoodApp.LoadInstallId).

        // the service control manager stops the daemon, and the tunnel must end with it
        appOptions.DisconnectOnDispose = true;

        device ??= new WindowsDevice(appOptions.StorageFolderPath, appOptions.IsDebugMode, serviceName: paths.ServiceName,
            vpnHostPath: paths.VpnHostPath ??
                         throw new ArgumentException("The daemon's app needs its VPN host.", nameof(paths)));
        return new VpnHoodWindowsApp(appOptions, device);
    }

    // VpnHoodApp.DisposeAsync disconnects first (DisconnectOnDispose); its plain Dispose would not.
    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync().Vhc();
        Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        // the daemon's stop deadline may run it while the graceful close ends with it
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        if (disposing) {
            // the app made here, not VpnHoodApp.Instance, which the app's own close may clear between two reads
            _app.Dispose();
            _singleInstanceLock?.Dispose();
            _singleInstanceLock = null;
        }

        base.Dispose(disposing);
    }
}
