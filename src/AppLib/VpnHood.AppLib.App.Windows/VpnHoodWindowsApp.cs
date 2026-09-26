using VpnHood.Core.Client.Devices.Windows;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Windows;

// The app as Windows runs it: one per app id on the machine, in a LocalSystem service. Init takes the
// single-instance lock before anything is built - a second daemon, started by hand while the service
// runs, touches nothing, not even the head's options - then starts the app. Stopping it is disposing
// it, and asynchronously, so the tunnel comes down before the process ends. The storage path is the
// host's (the service's folder under ProgramData), so the init params name no folder here. What a
// window shows of it - the tray, the window itself - runs in the person's own process, over the API.
public class VpnHoodWindowsApp : Singleton<VpnHoodWindowsApp>, IAsyncDisposable
{
    private static Semaphore? _singleInstanceLock;

    private VpnHoodWindowsApp(AppOptions appOptions)
    {
        VpnHoodApp.Init(new WindowsDevice(appOptions.StorageFolderPath, appOptions.IsDebugMode), appOptions);
    }

    public static VpnHoodWindowsApp Init(AppInitParams initParams, string storagePath)
    {
        // this process already holds the lock; taking it again would call itself another instance
        if (IsInit)
            return Instance;

        TakeSingleInstanceLock(initParams.AppId);

        Directory.CreateDirectory(storagePath);
        var context = new AppOptionsContext {
            AppId = initParams.AppId,
            StoragePath = storagePath,
            PackagedAssetProvider = new FolderAssetProvider(AppContext.BaseDirectory)
        };

        var appOptions = initParams.AppOptionsFactory(context);
        appOptions.DeviceUiProvider ??= new WindowsDeviceUiProvider();
        appOptions.EventWatcherInterval ??= TimeSpan.FromSeconds(1);

        // No DeviceId: the service's account is every machine's same S-1-5-18, so the app's own
        // Settings.ClientId serves.

        // the service control manager stops the daemon, and the tunnel must end with it
        appOptions.DisconnectOnDispose = true;

        return new VpnHoodWindowsApp(appOptions);
    }

    // A named semaphore rather than a mutex, which belongs to the thread that took it and would be
    // let go when that pool thread retires. Global, since the service runs in session 0 and a daemon
    // started by hand runs in a person's session. The kernel drops it with the process, so a crash
    // leaves no stale lock.
    private static void TakeSingleInstanceLock(string appId)
    {
        Semaphore semaphore;
        try {
            semaphore = new Semaphore(1, 1, @"Global\VpnHood-" + appId);
        }
        catch (UnauthorizedAccessException ex) {
            // it exists, and belongs to an instance this process may not even open
            throw new AnotherInstanceIsRunningException($"Another {appId} instance is already running.", ex);
        }

        if (!semaphore.WaitOne(TimeSpan.Zero)) {
            semaphore.Dispose();
            throw new AnotherInstanceIsRunningException($"Another {appId} instance is already running.");
        }

        _singleInstanceLock = semaphore;
    }

    // VpnHoodApp.DisposeAsync disconnects first (DisconnectOnDispose); its plain Dispose would not.
    public async ValueTask DisposeAsync()
    {
        if (VpnHoodApp.IsInit)
            await VpnHoodApp.Instance.DisposeAsync().Vhc();

        Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (VpnHoodApp.IsInit)
                VpnHoodApp.Instance.Dispose();

            _singleInstanceLock?.Release();
            _singleInstanceLock?.Dispose();
            _singleInstanceLock = null;
        }

        base.Dispose(disposing);
    }
}
