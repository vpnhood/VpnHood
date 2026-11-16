using VpnHood.Core.Client.Devices.WindowsTun;

namespace VpnHood.Core.Client.VpnServices.Host.WindowsTun;

/// <summary>
/// Represents a Windows device whose VPN service runs in this process, for a head that runs both halves in its one
/// process: a developer's (AvaloniaUI.Dev), whose debugger then follows the tunnel too, or a MAUI app's, which has no
/// daemon.
/// </summary>
/// <param name="storageFolder">The app's storage, with the VPN service's folder under it; the kill switch's owner.</param>
/// <param name="isDebugMode">
/// <see langword="true"/> to offer the split by app with test entries, as a debug build does.
/// </param>
/// <remarks>
/// A head with a daemon never makes one: it compiles against nothing of the VPN service's side, and its device starts
/// the VPN host (<c>docs/client-architecture.md</c>). The VPN service's lines go to the app's log, as <c>vpn.log</c> is the VPN
/// host's. A session's end disposes the VPN service; the next connect makes another.
/// </remarks>
public sealed class WindowsInProcessDevice(string storageFolder, bool isDebugMode)
    : WindowsDevice(storageFolder, isDebugMode)
{
    private WindowsVpnService? _vpnService;

    /// <inheritdoc/>
    /// <remarks>
    /// It makes the VPN service in this process, where it has none or its last has ended, and starts a connect in the
    /// background.
    /// </remarks>
    /// <exception cref="System.Net.Sockets.SocketException">The VPN service could not listen for the app.</exception>
    /// <exception cref="IOException">The VPN service could not publish its channel's file.</exception>
    public override Task StartVpnService(CancellationToken cancellationToken)
    {
        if (_vpnService is null || _vpnService.IsDisposed)
            _vpnService = new WindowsVpnService(VpnServiceConfigFolder, KillSwitch, withLogger: false);

        _vpnService.OnConnect();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It ends the VPN service in this process at once, if there is one.</remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _vpnService?.Dispose();
            _vpnService = null;
        }

        base.Dispose(disposing);
    }
}
