using System.ComponentModel;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.WindowsVpnPlugin;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.WindowsVpnPlugin;

// The app as the Windows VPN plug-in runs it: in the window's process, as the person, in the package's
// LocalState - the platform's, as iOS's App Group is, so the init params name no folder. The tunnel is
// Windows': the package's VPN host, which Windows starts when the app's VPN profile connects, holds it
// and outlives the app, so closing the app leaves the connection up and the next start finds it. While it
// runs, the app connects the VPN again after a sleep that ended it (WindowsVpnPluginSleepReconnect).
public class VpnHoodWindowsVpnPluginApp : Singleton<VpnHoodWindowsVpnPluginApp>
{
    private readonly FileStream _singleInstanceLock;
    private readonly VpnHoodApp _app;
    private readonly WindowsVpnPluginSleepReconnect? _sleepReconnect;

    private VpnHoodWindowsVpnPluginApp(FileStream singleInstanceLock, VpnHoodApp app,
        WindowsVpnPluginSleepReconnect? sleepReconnect)
    {
        _singleInstanceLock = singleInstanceLock;
        _app = app;
        _sleepReconnect = sleepReconnect;
    }

    public static VpnHoodWindowsVpnPluginApp Init(AppInitParams initParams)
    {
        // this process already holds the lock; taking it again would call itself another instance
        if (IsInit)
            return Instance;

        // One app per LocalState, which is the person's: the window's own claim keeps one per session,
        // and this one a second session of the same person from the same files.
        var storagePath = WindowsVpnPluginFolders.LocalFolder;
        var singleInstanceLock = InstanceLockFile.Take(storagePath, initParams.AppId);
        try {
            var context = new AppOptionsContext {
                AppId = initParams.AppId,
                AppName = initParams.AppName,
                AppVersion = initParams.AppVersion,
                StoragePath = storagePath,
                NoBackupStoragePath = storagePath, // nothing backs up a desktop app's data
                PackagedAssetProvider = new FolderAssetProvider(AppContext.BaseDirectory)
            };

            var appOptions = initParams.AppOptionsFactory(context);
            appOptions.DeviceUiProvider ??= new WindowsVpnPluginDeviceUiProvider();

            // the profile in Windows' VPN settings carries the app's name, as iOS's configuration does
            var app = VpnHoodApp.Init(new WindowsVpnPluginDevice(profileName: appOptions.AppName), appOptions);
            return new VpnHoodWindowsVpnPluginApp(singleInstanceLock, app, CreateSleepReconnect(app));
        }
        catch {
            singleInstanceLock.Dispose();
            throw;
        }
    }

    // A machine that will not say when it sleeps leaves the app without it, which the log says.
    private static WindowsVpnPluginSleepReconnect? CreateSleepReconnect(VpnHoodApp app)
    {
        try {
            return new WindowsVpnPluginSleepReconnect(app);
        }
        catch (Win32Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not watch the machine's sleeps, so the VPN is not connected again after one.");
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _sleepReconnect?.Dispose();
            // the app made here, not VpnHoodApp.Instance, which the app's own close may clear between two reads
            _app.Dispose();
            _singleInstanceLock.Dispose();
        }

        base.Dispose(disposing);
    }
}
