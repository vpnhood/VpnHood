using VpnHood.Core.Client.VpnServices.Host;

namespace VpnHood.Core.Client.Devices.WindowsTun;

// The machine's one tunnel on Windows: a mutex in an administrators' namespace that every app built on
// VpnHood shares, and beside it an event by which an app asks the holder to let go. Only SYSTEM and an
// elevated administrator may enter the namespace, so a standard user can neither hold the tunnel nor
// ask for it. A crash abandons the mutex, and the next app takes it as it is; WinTun removes a dead
// process's adapter itself.
public sealed class WindowsVpnTunnelLock : IVpnTunnelLock
{
    public Task<IDisposable> Take(IVpnTunnelHolder holder, CancellationToken cancellationToken)
    {
        return WindowsVpnTunnelHold.Take(holder, cancellationToken);
    }
}
