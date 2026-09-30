using VpnHood.Core.Client.Devices.Windows;
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
    private static IDisposable? _singleInstanceLock;

    private VpnHoodWindowsApp(AppOptions appOptions)
    {
        VpnHoodApp.Init(new WindowsDevice(appOptions.StorageFolderPath, appOptions.IsDebugMode), appOptions);
    }

    // lockFolderPath: where a run as the person keeps its single-instance lock, a folder of their own
    // (InstanceLockFile); null for the service, whose lock only administrators can reach.
    public static VpnHoodWindowsApp Init(AppInitParams initParams, string storagePath, string? lockFolderPath)
    {
        // this process already holds the lock; taking it again would call itself another instance
        if (IsInit)
            return Instance;

        _singleInstanceLock = lockFolderPath is null
            ? ServiceInstanceLock.Take(initParams.AppId)
            : InstanceLockFile.Take(lockFolderPath, initParams.AppId);

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

            _singleInstanceLock?.Dispose();
            _singleInstanceLock = null;
        }

        base.Dispose(disposing);
    }
}
