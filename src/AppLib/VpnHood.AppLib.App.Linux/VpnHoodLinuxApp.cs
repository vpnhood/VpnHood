using System.Net.Sockets;
using VpnHood.Core.Client.Devices.Linux;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.AppLib.App.Linux;

// The app as Linux runs it: one per app id on the machine, in a root service. Init takes the
// single-instance lock before anything is built - a second daemon, started by hand or by a unit
// restarted while one still runs, touches nothing, not even the head's options - then clears the
// tuns a crashed run left, and starts the app. Stopping it is disposing it, and
// asynchronously, so the tunnel comes down before the process ends. The storage path is the
// host's (the daemon's folder beside its versions), so the init params name no folder here.
public class VpnHoodLinuxApp : Singleton<VpnHoodLinuxApp>, IAsyncDisposable
{
    private static Socket? _singleInstanceSocket;

    private VpnHoodLinuxApp(AppOptions appOptions)
    {
        VpnHoodApp.Init(new LinuxDevice(appOptions.StorageFolderPath), appOptions);
    }

    public static VpnHoodLinuxApp Init(AppInitParams initParams, string storagePath)
    {
        // this process already holds the lock; binding again would call itself another instance
        if (IsInit)
            return Instance;

        TakeSingleInstanceLock(initParams.AppId);

        // after the lock, so a tun of this app id is never one another of its instances is using
        LinuxTunVpnAdapter.RemoveLeftovers(initParams.AppId);

        Directory.CreateDirectory(storagePath);
        var context = new AppOptionsContext {
            AppId = initParams.AppId,
            StoragePath = storagePath,
            PackagedAssetProvider = new FolderAssetProvider(AppContext.BaseDirectory)
        };

        var appOptions = initParams.AppOptionsFactory(context);

        // the service manager stops the daemon with a signal, and the tunnel must end with it
        appOptions.DisconnectOnDispose = true;

        return new VpnHoodLinuxApp(appOptions);
    }

    // An abstract Unix socket - its name starts with '\0' - which the kernel releases with the
    // process: a crash leaves no stale lock, and a second bind fails while the first process lives.
    private static void TakeSingleInstanceLock(string appId)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try {
            socket.Bind(new UnixDomainSocketEndPoint("\0singleton-" + appId));
            socket.Listen(1);
            _singleInstanceSocket = socket;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse) {
            socket.Dispose();
            throw new AnotherInstanceIsRunningException($"Another {appId} instance is already running.", ex);
        }
        catch {
            socket.Dispose();
            throw;
        }
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

            _singleInstanceSocket?.Dispose();
            _singleInstanceSocket = null;
        }

        base.Dispose(disposing);
    }
}
