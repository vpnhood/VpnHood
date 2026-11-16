
namespace VpnHood.Core.Client.VpnServices.Host.WindowsTun;

/// <summary>
/// Represents the machine's one tunnel on Windows: a mutex in an administrators' namespace that every app built on
/// VpnHood shares, and beside it an event by which an app asks the holder to let go.
/// </summary>
/// <remarks>
/// The namespace is meant to keep a standard user from holding the tunnel or asking for it. A crash abandons the mutex,
/// and the next app takes it as it is; WinTun removes a dead process's adapter itself.
/// </remarks>
internal sealed class WindowsVpnTunnelLock : IVpnTunnelLock
{
    /// <inheritdoc/>
    /// <remarks>
    /// It holds the mutex on a thread of its own. Where another app holds it, it asks that app to let go each second,
    /// for up to 30 seconds.
    /// </remarks>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused the administrators' namespace.</exception>
    public Task<IDisposable> Take(IVpnTunnelHolder holder, CancellationToken cancellationToken)
    {
        return WindowsVpnTunnelHold.Take(holder, cancellationToken);
    }
}
