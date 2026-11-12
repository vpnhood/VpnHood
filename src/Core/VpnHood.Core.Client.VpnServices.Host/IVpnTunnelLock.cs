namespace VpnHood.Core.Client.VpnServices.Host;

/// <summary>
/// Defines the machine's one tunnel, which every app built on VpnHood takes before it brings an adapter up, so a new
/// connect ends the tunnel already up, whichever app made it.
/// </summary>
/// <remarks>
/// As on Android, the new app waits for nothing of the old one but its adapter: the holder drops it and lets go at
/// once. A crash lets it go with the process, the adapter with it. Only the desktops have one: Android and iOS hand the
/// tunnel from one VPN app to another themselves.
/// </remarks>
public interface IVpnTunnelLock
{
    /// <summary>Takes the tunnel for a holder.</summary>
    /// <param name="holder">The holder, which hears once if another app asks for the tunnel.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is the hold: the tunnel is the holder's until the hold is disposed, or until an app asks for
    /// it, when the hold lets go as soon as the holder returns.
    /// </returns>
    /// <exception cref="TimeoutException">The app that holds the tunnel did not let it go in time.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IDisposable> Take(IVpnTunnelHolder holder, CancellationToken cancellationToken);
}
