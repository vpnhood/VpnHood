using VpnHood.Core.Client.Devices.WindowsTun;

namespace VpnHood.Core.Client.VpnServices.Host.WindowsTun;

// A Windows device whose VPN service runs in this process, for a head that runs both halves in its one process: a
// developer's (AvaloniaUI.Dev), whose debugger then follows the tunnel too, or a MAUI app's, which has no daemon. An app
// head never makes one: it compiles against nothing of the VPN service's side, and its device starts the VPN host
// (docs/client-architecture.md). The VPN service's lines go to the app's log, as vpn.log is the VPN host's. A session's
// end disposes the VPN service; the next connect makes another.
public sealed class WindowsInProcessDevice(string storageFolder, bool isDebugMode)
    : WindowsDevice(storageFolder, isDebugMode)
{
    private WindowsVpnService? _vpnService;

    public override Task StartVpnService(CancellationToken cancellationToken)
    {
        if (_vpnService is null || _vpnService.IsDisposed)
            _vpnService = new WindowsVpnService(VpnServiceConfigFolder, KillSwitch, withLogger: false);

        _vpnService.OnConnect();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _vpnService?.Dispose();
            _vpnService = null;
        }

        base.Dispose(disposing);
    }
}
