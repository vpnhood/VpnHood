using VpnHood.AppLib.App.Utils;
using VpnHood.Core.Client.Devices.Linux;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.AppLib.App.Linux;

// The app as Linux runs it: one per app id on the machine, in a root service. Init takes the
// single-instance lock before anything is built - a second daemon, started by hand or by a unit
// restarted while one still runs, touches nothing, not even the head's options - then starts the
// app and clears what a crashed run left. Stopping it is disposing it, and asynchronously, so the
// tunnel comes down before the process ends. The storage path is the host's (the daemon's folder
// beside its versions), so the init params name no folder here.
public class VpnHoodLinuxApp : Singleton<VpnHoodLinuxApp>, IAsyncDisposable
{
    private static FileStream? _singleInstanceLock;
    private readonly VpnHoodApp _app;
    private bool _disposed;

    private VpnHoodLinuxApp(AppOptions appOptions)
    {
        _app = VpnHoodApp.Init(new LinuxDevice(appOptions.StorageFolderPath), appOptions);
    }

    // lockFolderPath: where the single-instance lock lives (InstanceLockFile), which the host names -
    // the service's folder under /run, which only root may write, so nobody takes the lock first, or a
    // debugger's run's own storage. Not an abstract socket name, which anyone may take first.
    public static VpnHoodLinuxApp Init(AppInitParams initParams, string storagePath, string lockFolderPath)
    {
        // this process already holds the lock; taking it again would call itself another instance
        if (IsInit)
            return Instance;

        _singleInstanceLock = InstanceLockFile.Take(lockFolderPath, initParams.AppId);

        // its owner's alone, as the installer makes it; one made here is made so too
        Directory.CreateDirectory(storagePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var context = new AppOptionsContext {
            AppId = initParams.AppId,
            AppName = initParams.AppName,
            AppVersion = initParams.AppVersion,
            StoragePath = storagePath,
            NoBackupStoragePath = storagePath, // nothing backs up a desktop app's data
            PackagedAssetProvider = new FolderAssetProvider(AppContext.BaseDirectory)
        };

        var appOptions = initParams.AppOptionsFactory(context);

        // a server may have no window or command asking for the state, and always on must still see a
        // session end
        appOptions.EventWatcherInterval ??= TimeSpan.FromSeconds(1);

        // the service manager stops the daemon with a signal, and the tunnel must end with it
        appOptions.DisconnectOnDispose = true;

        // after the lock, so a tun of this app id is never one another of its instances is using, and before
        // the app, whose always on connects as it starts; the adapter's name as LinuxVpnService gives it
        LinuxTunVpnAdapter.RemoveLeftovers(initParams.AppId,
            LinuxTunVpnAdapter.GetValidAdapterName(AppUtils.GetAdapterName(appOptions)));

        return new VpnHoodLinuxApp(appOptions);
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
